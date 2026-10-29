import ctypes
import ctypes.wintypes
import os
import sys
import tempfile
import threading
import time

# Constants
GENERIC_ALL = 0x10000000
HANDLE_FLAG_INHERIT = 1
STARTF_USESTDHANDLES = 0x100
CREATE_UNICODE_ENVIRONMENT = 0x400
WAIT_TIMEOUT = 0x102
WAIT_OBJECT_0 = 0x0

# Load libraries
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
user32 = ctypes.WinDLL("user32", use_last_error=True)

# Define Structures
class STARTUPINFOW(ctypes.Structure):
    _fields_ = [
        ("cb", ctypes.wintypes.DWORD),
        ("lpReserved", ctypes.wintypes.LPWSTR),
        ("lpDesktop", ctypes.wintypes.LPWSTR),
        ("lpTitle", ctypes.wintypes.LPWSTR),
        ("dwX", ctypes.wintypes.DWORD),
        ("dwY", ctypes.wintypes.DWORD),
        ("dwXSize", ctypes.wintypes.DWORD),
        ("dwYSize", ctypes.wintypes.DWORD),
        ("dwXCountChars", ctypes.wintypes.DWORD),
        ("dwYCountChars", ctypes.wintypes.DWORD),
        ("dwFillAttribute", ctypes.wintypes.DWORD),
        ("dwFlags", ctypes.wintypes.DWORD),
        ("wShowWindow", ctypes.wintypes.WORD),
        ("cbReserved2", ctypes.wintypes.WORD),
        ("lpReserved2", ctypes.POINTER(ctypes.c_byte)),
        ("hStdInput", ctypes.wintypes.HANDLE),
        ("hStdOutput", ctypes.wintypes.HANDLE),
        ("hStdError", ctypes.wintypes.HANDLE),
    ]

class PROCESS_INFORMATION(ctypes.Structure):
    _fields_ = [
        ("hProcess", ctypes.wintypes.HANDLE),
        ("hThread", ctypes.wintypes.HANDLE),
        ("dwProcessId", ctypes.wintypes.DWORD),
        ("dwThreadId", ctypes.wintypes.DWORD),
    ]

# Set argtypes and restypes
user32.CreateDesktopW.argtypes = [
    ctypes.wintypes.LPWSTR,
    ctypes.wintypes.LPVOID,
    ctypes.wintypes.LPVOID,
    ctypes.wintypes.DWORD,
    ctypes.wintypes.DWORD,
    ctypes.wintypes.LPVOID
]
user32.CreateDesktopW.restype = ctypes.wintypes.HANDLE

user32.CloseDesktop.argtypes = [ctypes.wintypes.HANDLE]
user32.CloseDesktop.restype = ctypes.wintypes.BOOL

kernel32.SetHandleInformation.argtypes = [
    ctypes.wintypes.HANDLE,
    ctypes.wintypes.DWORD,
    ctypes.wintypes.DWORD
]
kernel32.SetHandleInformation.restype = ctypes.wintypes.BOOL

kernel32.CreateProcessW.argtypes = [
    ctypes.wintypes.LPWSTR,
    ctypes.wintypes.LPWSTR,
    ctypes.wintypes.LPVOID,
    ctypes.wintypes.LPVOID,
    ctypes.wintypes.BOOL,
    ctypes.wintypes.DWORD,
    ctypes.wintypes.LPVOID,
    ctypes.wintypes.LPWSTR,
    ctypes.POINTER(STARTUPINFOW),
    ctypes.POINTER(PROCESS_INFORMATION)
]
kernel32.CreateProcessW.restype = ctypes.wintypes.BOOL

kernel32.WaitForSingleObject.argtypes = [
    ctypes.wintypes.HANDLE,
    ctypes.wintypes.DWORD
]
kernel32.WaitForSingleObject.restype = ctypes.wintypes.DWORD

kernel32.TerminateProcess.argtypes = [
    ctypes.wintypes.HANDLE,
    ctypes.wintypes.ULONG
]
kernel32.TerminateProcess.restype = ctypes.wintypes.BOOL

kernel32.GetExitCodeProcess.argtypes = [
    ctypes.wintypes.HANDLE,
    ctypes.POINTER(ctypes.wintypes.ULONG)
]
kernel32.GetExitCodeProcess.restype = ctypes.wintypes.BOOL

kernel32.CloseHandle.argtypes = [ctypes.wintypes.HANDLE]
kernel32.CloseHandle.restype = ctypes.wintypes.BOOL

# Thread-safe counter for unique desktop names
_counter_lock = threading.Lock()
_counter = 0

def _get_unique_name():
    global _counter
    with _counter_lock:
        _counter += 1
        return "bm21hidden_" + str(os.getpid()) + "_" + str(_counter)

def run_hidden(args, timeout=180, env=None):
    if not args:
        raise ValueError("args must not be empty")

    desktop_name = _get_unique_name()

    # 1. Create Desktop
    hDesktop = user32.CreateDesktopW(desktop_name, None, None, 0, GENERIC_ALL, None)
    if not hDesktop:
        raise ctypes.WinError()

    # 2. Create temp file for output
    fd, filepath = tempfile.mkstemp()
    hFile = None
    try:
        # Get OS handle
        import msvcrt
        hFile = msvcrt.get_osfhandle(fd)

        # Make handle inheritable
        if not kernel32.SetHandleInformation(hFile, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT):
            raise ctypes.WinError()

        # 3. Build STARTUPINFOW
        si = STARTUPINFOW()
        si.cb = ctypes.sizeof(STARTUPINFOW)
        si.lpDesktop = desktop_name
        si.dwFlags = STARTF_USESTDHANDLES
        si.hStdInput = None
        si.hStdOutput = hFile
        si.hStdError = hFile

        pi = PROCESS_INFORMATION()

        # 4. Build command line and environment
        cmdline = subprocess.list2cmdline(args)
        cmdline_buf = ctypes.create_unicode_buffer(cmdline)

        flags = 0
        env_block = None
        if env is not None:
            flags |= CREATE_UNICODE_ENVIRONMENT
            # Build UTF-16 environment block: KEY=VALUE\0...\0\0
            env_strs = [f"{k}={v}" for k, v in env.items()]
            env_block_str = "\0".join(env_strs) + "\0\0"
            env_block = ctypes.create_unicode_buffer(env_block_str)

        # Call CreateProcessW
        if not kernel32.CreateProcessW(
            None,
            cmdline_buf,
            None,
            None,
            True,  # bInheritHandles
            flags,
            env_block,
            None,
            ctypes.byref(si),
            ctypes.byref(pi)
        ):
            raise ctypes.WinError()

        # 5. Wait for process
        wait_result = kernel32.WaitForSingleObject(pi.hProcess, int(timeout * 1000))

        timed_out = False
        if wait_result == WAIT_TIMEOUT:
            timed_out = True
            kernel32.TerminateProcess(pi.hProcess, 1)
            kernel32.WaitForSingleObject(pi.hProcess, 5000)

        # 6. Get exit code
        exit_code = ctypes.wintypes.ULONG()
        kernel32.GetExitCodeProcess(pi.hProcess, ctypes.byref(exit_code))

        # Close process handles
        kernel32.CloseHandle(pi.hProcess)
        kernel32.CloseHandle(pi.hThread)

        # Close file
        os.close(fd)
        fd = -1  # Mark as closed

        # Close desktop
        user32.CloseDesktop(hDesktop)
        hDesktop = None

        # Read file contents
        with open(filepath, 'rb') as f:
            data = f.read()

        text = data.decode('utf-8', errors='replace')
        text = text.replace('\r\n', '\n')

        # Delete file
        try:
            os.remove(filepath)
        except OSError:
            pass

        if timed_out:
            return None
        else:
            return (exit_code.value, text)

    finally:
        # Cleanup in case of exception
        if fd != -1:
            try:
                os.close(fd)
            except OSError:
                pass
        if hDesktop:
            user32.CloseDesktop(hDesktop)
        try:
            os.remove(filepath)
        except OSError:
            pass

# Import subprocess for list2cmdline
import subprocess

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python module.py <program> [args...]")
        sys.exit(1)
    result = run_hidden(sys.argv[1:])
    print(result)
