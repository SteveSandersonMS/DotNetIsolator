#include <assert.h>
#include <mono-wasi/driver.h>

__attribute__((import_module("dotnetisolator")))
__attribute__((import_name("set_timeout")))
void dotnetisolator_set_timeout(int timeout);

__attribute__((import_module("dotnetisolator")))
__attribute__((import_name("queue_callback")))
void dotnetisolator_queue_callback();

void dotnetisolator_add_threadpool_callbacks() {
	mono_add_internal_call("System.Threading.TimerQueue::SetTimeout", dotnetisolator_set_timeout);
	mono_add_internal_call("System.Threading.ThreadPool::QueueCallback", dotnetisolator_queue_callback);
}
