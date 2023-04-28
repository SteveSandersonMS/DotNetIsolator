#include <assert.h>
#include <stdio.h>
#include <mono/metadata/class.h>
#include <mono-wasi/driver.h>

typedef struct RunnerInvocation {
	MonoGCHandle target;
	MonoMethod* method_ptr;
	MonoString* result_exception;
	int result_type;
	void* result_ptr;
	int result_length;
	MonoGCHandle result_handle;
	void** args_length_prefixed_buffers;
	int args_length_prefixed_buffers_length;
} RunnerInvocation;

#define RESULT_TYPE_SERIALIZE 0
#define RESULT_TYPE_HANDLE 1

__attribute__((export_name("dotnetisolator_instantiate_class")))
MonoGCHandle dotnetisolator_instantiate_class(MonoClass* class) {
	MonoObject* instance = mono_object_new(NULL, class);
	MonoGCHandle result = (MonoGCHandle)mono_gchandle_new(instance, /* pinned */ 0);
	mono_runtime_object_init(instance);

	return result;
}

__attribute__((export_name("dotnetisolator_release_object")))
void dotnetisolator_release_object(MonoGCHandle gcHandle) {
	if (gcHandle) {
		mono_gchandle_free((uint32_t)gcHandle);
	}
}

__attribute__((export_name("dotnetisolator_get_class")))
MonoClass* dotnetisolator_get_class(MonoGCHandle gcHandle) {
	if (gcHandle) {
		MonoObject* object = mono_gchandle_get_target((uint32_t)(gcHandle));
		if (object) {
			return mono_object_get_class(object);
		}
	}
	return NULL;
}

__attribute__((export_name("dotnetisolator_lookup_class")))
MonoClass* dotnetisolator_lookup_class(char* assembly_name, char* namespace, char* type_name, char** error_msg) {
	//printf("Trying to find %s %s %s\n", assembly_name, namespace, type_name);
	MonoClass* result = NULL;
	*error_msg = NULL;
	char* namespace_or_fallback = namespace ? namespace : "(no namespace)";

	MonoAssembly* assembly = mono_wasm_assembly_load(assembly_name);
	if (!assembly) {
		asprintf(error_msg, "Could not find class [%s]%s.%s because the assembly %s could not be found.", assembly_name, namespace_or_fallback, type_name, assembly_name);
	} else {
		result = mono_wasm_assembly_find_class(assembly, namespace, type_name);
		if (!result) {
			asprintf(error_msg, "Could not find class [%s]%s.%s because the type %s could not be found in the assembly.", assembly_name, namespace_or_fallback, type_name, type_name);
		}
	}

	free(assembly_name);
	free(namespace);
	free(type_name);
	return result;
}

__attribute__((export_name("dotnetisolator_lookup_method")))
MonoMethod* dotnetisolator_lookup_method(MonoClass* class, char* method_name, int num_params, char** error_msg) {
	//printf("Trying to find %s %i\n", method_name, num_params);
	*error_msg = NULL;

	MonoMethod* result = mono_wasm_assembly_find_method(class, method_name, num_params);
	if (!result) {
		asprintf(error_msg, "Could not find method %s in the type with the correct number of parameters.", method_name);
	}

	free(method_name);
	return result;
}

MonoMethod* deserialize_param_dotnet_method;
MonoMethod* serialize_return_value_dotnet_method;

void* deserialize_param(void* length_prefixed_buffer, MonoGCHandle* value_handle, MonoObject** exception_buf) {
	if (!length_prefixed_buffer) {
		return NULL;
	}

	if (deserialize_param_dotnet_method == 0) {
		deserialize_param_dotnet_method = lookup_dotnet_method("DotNetIsolator.WasmApp", "DotNetIsolator.WasmApp", "Serialization", "Deserialize", -1);
	}

	void* method_params[] = { length_prefixed_buffer + 4, length_prefixed_buffer };
	MonoObject* result = mono_wasm_invoke_method(
		deserialize_param_dotnet_method,
		NULL,
		method_params,
		exception_buf);

	if (!result) {
		*value_handle = NULL;
		return NULL;
	}

	// I don't actually know for sure if it's necessary to pin these MonoObject* for the duration between
	// deserializing and calling the method. But given that we're unboxing value types and getting back a
	// raw pointer to the value-type memory, we do need it not to move in this period.
	*value_handle = (MonoGCHandle)mono_gchandle_new(result, /* pinned */ 1);

	int must_unbox = mono_class_is_valuetype(mono_object_get_class(result));
	return must_unbox ? mono_object_unbox(result) : result;
}

void serialize_return_value(MonoObject* value, RunnerInvocation* invocation, MonoObject** exception_buf) {
	if (!value) {
		invocation->result_ptr = NULL;
		return;
	}

	if (invocation->result_type == RESULT_TYPE_HANDLE) {
		invocation->result_ptr = mono_object_get_class(value);
		invocation->result_handle = (MonoGCHandle)mono_gchandle_new(value, /* pinned */ 0);
		return;
	}

	if (serialize_return_value_dotnet_method == 0) {
		serialize_return_value_dotnet_method = lookup_dotnet_method("DotNetIsolator.WasmApp", "DotNetIsolator.WasmApp", "Serialization", "Serialize", -1);
	}

	void* method_params[] = { value };
	MonoObject* byte_array = mono_wasm_invoke_method(serialize_return_value_dotnet_method, NULL, method_params, exception_buf);
	invocation->result_ptr = mono_array_addr_with_size((MonoArray*)byte_array, 1, 0);
	invocation->result_length = mono_array_length((MonoArray*)byte_array);
	invocation->result_handle = (MonoGCHandle)mono_gchandle_new(byte_array, /* pinned */ 1);
}

__attribute__((export_name("dotnetisolator_invoke_method")))
void dotnetisolator_invoke_method(RunnerInvocation* invocation) {
	MonoObject* exc = NULL;

	int num_args = invocation->args_length_prefixed_buffers_length;
	void* method_params[num_args];
	MonoGCHandle arg_handles[num_args];
	for (int i = 0; i < num_args; i++) {
		void* arg_length_prefixed_buffer = invocation->args_length_prefixed_buffers[i];
		method_params[i] = deserialize_param(arg_length_prefixed_buffer, &arg_handles[i], &exc);
		if (exc) {
			break;
		}
	}

	free(invocation->args_length_prefixed_buffers);

	if (!exc) {
		//printf("GCHandle: %p, Method: %p, Arg0: %p\n", invocation->target, invocation->method_ptr, invocation->arg0);
		MonoObject* target = invocation->target ? mono_gchandle_get_target((uint32_t)(invocation->target)) : 0;

		MonoObject* result;
		if (invocation->method_ptr) {
			if (target) {
				MonoMethod* method = mono_object_get_virtual_method(target, invocation->method_ptr);
				if (method) {
					invocation->method_ptr = method;
				}
			}
			result = mono_wasm_invoke_method(invocation->method_ptr, target, method_params, &exc);
		} else {
			result = target;
		}

		for (int i = 0; i < num_args; i++) {
			if (arg_handles[i]) {
				mono_gchandle_free((uint32_t)arg_handles[i]);
			}
		}

		if (!exc) {
			serialize_return_value(result, invocation, &exc);
		}
	}

	if (exc) {
		// Return a string representation of the error
		MonoObject* ignored_tostring_exception;
		invocation->result_exception = mono_object_to_string(exc, &ignored_tostring_exception);
	}
}

__attribute__((export_name("dotnetisolator_deserialize_object")))
MonoGCHandle dotnetisolator_deserialize_object(void* length_prefixed_buffer, MonoString** error_monostring) {
	//printf("addr: %p; len: %i; first: %i\n", length_prefixed_buffer, ((int*)length_prefixed_buffer)[0], ((unsigned char*)length_prefixed_buffer)[4]);
	MonoGCHandle result;
	MonoObject* deserialization_exception = NULL;
	deserialize_param(length_prefixed_buffer, &result, &deserialization_exception);

	if (deserialization_exception) {
		// Return a string representation of the error
		MonoObject* ignored_tostring_exception;
		*error_monostring = mono_object_to_string(deserialization_exception, &ignored_tostring_exception);
		return NULL;
	} else {
		*error_monostring = NULL;
		return result;
	}
}
