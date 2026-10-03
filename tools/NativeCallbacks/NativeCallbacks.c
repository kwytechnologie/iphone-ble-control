#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#ifndef _WIN64
#error This companion is built for Windows x64 only.
#endif

/* Same ABI as SW_DEVICE_CREATE_CALLBACK; the device handle is opaque here.
 * SwDeviceCreate retains the PE module containing its callback. A .NET JIT
 * delegate thunk is not in a PE module, so it cannot be passed directly.
 * The owning helper must keep the managed delegate and this DLL alive until
 * SwDeviceClose has returned. There is no global callback or device state. */
typedef VOID (CALLBACK *MANAGED_CREATE_CALLBACK)(
    PVOID device, HRESULT result, PVOID context, PCWSTR instance_id);

__declspec(dllexport) VOID CALLBACK BleHidSwDeviceCreated(
    PVOID device, HRESULT result, PVOID context, PCWSTR instance_id)
{
    if (context != NULL)
        ((MANAGED_CREATE_CALLBACK)context)(device, result, NULL, instance_id);
}
