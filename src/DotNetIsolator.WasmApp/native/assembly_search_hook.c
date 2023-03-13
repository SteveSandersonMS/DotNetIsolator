#include <mono-wasi/driver.h>
#include <mono/metadata/class.h>
#include <string.h>
#include <assert.h>

__attribute__((import_module("dotnetisolator")))
__attribute__((import_name("request_assembly")))
int request_assembly(const char* assembly_name, int assembly_name_len, void** supplied_bytes, int* supplied_bytes_len);

struct _MonoAssemblyName {
	// There are other members too - see `metadata-internals.h` - but this is all we need now
	const char* name;
};

// mono_assembly_load_from also calls the search hooks, so we'll get into an infinite recursive loop
// unless we explicitly stop the search hook from running inside itself
int assembly_search_hook_in_progress = 0;

MonoAssembly* dotnetisolator_assembly_search_hook(MonoAssemblyName* aname, void* user_data) {
	MonoAssembly* result = NULL;

	if (!assembly_search_hook_in_progress && !getenv("DISABLE_ASSEMBLY_SEARCH_HOOK")) {
		//printf("In dotnetisolator_assembly_search_hook for %s\n", aname->name);
		void* loaded_bytes;
		int loaded_bytes_len;
		int success = request_assembly(aname->name, strlen(aname->name), &loaded_bytes, &loaded_bytes_len);
		if (success) {
			MonoImageOpenStatus status;
			MonoImage* image = mono_image_open_from_data(loaded_bytes, loaded_bytes_len, 1, &status);

			assembly_search_hook_in_progress = 1;
			result = mono_assembly_load_from(image, aname->name, &status);
			assembly_search_hook_in_progress = 0;
		}
	}

	return result;
}

void dotnetisolator_add_assembly_search_hook() {
	mono_install_assembly_search_hook(dotnetisolator_assembly_search_hook, NULL);
}
