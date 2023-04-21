#include <stdio.h>
#include <mono-wasi/driver.h>

__attribute__((import_module("dotnetisolator")))
__attribute__((import_name("call_host")))
void dotnetisolator_call_host_import();

void dotnetisolator_call_host() {
	dotnetisolator_call_host_import();
}

void dotnetisolator_add_host_callback_internal_calls() {
	mono_add_internal_call("DotNetIsolator.Guest.Interop::CallHost", dotnetisolator_call_host);
}
