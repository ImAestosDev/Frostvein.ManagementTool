// FMT cursor shim: a d3d9.dll that sits in front of DXVK (fmt_dxvk.dll) or the system d3d9.dll.
// NosTale sets plain Windows cursors with SetCursor. The shim redirects that one import of the game exe
// (in memory only, the file stays untouched) and swaps each game cursor for fmt-cursors\<hash>.cur,
// or fmt-cursors\default.cur when there is no match. Everything else goes straight through.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d9.h>
#include <stdio.h>
#include <stdint.h>

// FMT finds its own dll by this text.
extern "C" const char FmtMarker[] = "FMT-CURSOR-SHIM";

static HMODULE g_real;
static wchar_t g_dir[MAX_PATH];  // fmt-cursors folder next to the game, with a trailing slash
static bool g_active;            // fmt-cursors\on exists

typedef IDirect3D9* (WINAPI* Create9Fn)(UINT);
typedef HRESULT (WINAPI* Create9ExFn)(UINT, IDirect3D9Ex**);
typedef HCURSOR (WINAPI* SetCursorFn)(HCURSOR);
static SetCursorFn g_setCursor = &SetCursor;

struct Swap { HCURSOR game, own; };
static Swap g_swaps[128];
static int g_swapCount, g_swapNext;
static CRITICAL_SECTION g_lock;

static bool Exists(const wchar_t* path) { return GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES; }

// Pixels of a cursor as top-down 32-bit rows; monochrome cursors give their AND/XOR mask instead.
static int Pixels(HCURSOR cursor, uint8_t* out, int max, int& width, int& height, ICONINFO& info)
{
    if (!GetIconInfo(cursor, &info)) return 0;
    HBITMAP source = info.hbmColor ? info.hbmColor : info.hbmMask;
    BITMAP bm = {};
    GetObjectW(source, sizeof(bm), &bm);
    width = bm.bmWidth;
    height = bm.bmHeight;
    int size = width * height * 4;
    if (size <= 0 || size > max) return 0;
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(bi.bmiHeader);
    bi.bmiHeader.biWidth = width;
    bi.bmiHeader.biHeight = -height;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    HDC dc = GetDC(nullptr);
    int lines = GetDIBits(dc, source, 0, height, out, &bi, DIB_RGB_COLORS);
    ReleaseDC(nullptr, dc);
    return lines ? size : 0;
}

// Keeps every cursor the game uses as a .bmp, so FMT can map it to the pack.
static void Dump(uint32_t hash, const uint8_t* data, int width, int height, const ICONINFO& info)
{
    wchar_t path[MAX_PATH];
    swprintf_s(path, L"%sgame", g_dir);
    CreateDirectoryW(path, nullptr);
    swprintf_s(path, L"%sgame\\%08X_%lu_%lu.bmp", g_dir, hash, info.xHotspot, info.yHotspot);
    if (Exists(path)) return;
    FILE* f = nullptr;
    if (_wfopen_s(&f, path, L"wb") != 0 || !f) return;
    BITMAPFILEHEADER fh = { 0x4D42, 0, 0, 0, sizeof(BITMAPFILEHEADER) + sizeof(BITMAPINFOHEADER) };
    BITMAPINFOHEADER ih = { sizeof(ih), width, -height, 1, 32, BI_RGB };
    fh.bfSize = fh.bfOffBits + width * height * 4;
    fwrite(&fh, sizeof(fh), 1, f);
    fwrite(&ih, sizeof(ih), 1, f);
    fwrite(data, 4, width * height, f);
    fclose(f);
}

static HCURSOR Replacement(HCURSOR game)
{
    static uint8_t pixels[256 * 256 * 4];
    int width = 0, height = 0;
    ICONINFO info = {};
    int size = Pixels(game, pixels, sizeof(pixels), width, height, info);
    if (info.hbmColor) DeleteObject(info.hbmColor);
    if (info.hbmMask) DeleteObject(info.hbmMask);
    if (!size) return nullptr;
    uint32_t hash = 2166136261u;
    for (int i = 0; i < size; i++) { hash ^= pixels[i]; hash *= 16777619u; }
    Dump(hash, pixels, width, height, info);
    if (!g_active) return nullptr;
    wchar_t path[MAX_PATH];
    swprintf_s(path, L"%s%08X.cur", g_dir, hash);
    if (!Exists(path)) swprintf_s(path, L"%sdefault.cur", g_dir);
    return Exists(path) ? (HCURSOR)LoadImageW(nullptr, path, IMAGE_CURSOR, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE) : nullptr;
}

static HCURSOR WINAPI HookSetCursor(HCURSOR cursor)
{
    if (!cursor) return g_setCursor(cursor);
    HCURSOR own = nullptr;
    bool known = false;
    EnterCriticalSection(&g_lock);
    for (int i = 0; i < g_swapCount; i++)
        if (g_swaps[i].game == cursor) { own = g_swaps[i].own; known = true; break; }
    if (!known)
    {
        own = Replacement(cursor);
        // Ring buffer: a game that builds new cursors all the time cannot grow this.
        Swap& slot = g_swaps[g_swapNext];
        if (g_swapCount == 128 && slot.own) DestroyCursor(slot.own);
        slot = { cursor, own };
        g_swapNext = (g_swapNext + 1) % 128;
        if (g_swapCount < 128) g_swapCount++;
    }
    LeaveCriticalSection(&g_lock);
    return g_setCursor(own ? own : cursor);
}

// Points the game exe's SetCursor import at the hook. Only the import table in memory changes.
static void HookImports()
{
    auto base = (uint8_t*)GetModuleHandleW(nullptr);
    auto dos = (IMAGE_DOS_HEADER*)base;
    auto nt = (IMAGE_NT_HEADERS*)(base + dos->e_lfanew);
    auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return;
    HMODULE user = GetModuleHandleW(L"user32.dll");
    struct { void* target; void* hook; } hooks[] =
    {
        { GetProcAddress(user, "SetCursor"), (void*)&HookSetCursor },
    };
    for (auto imp = (IMAGE_IMPORT_DESCRIPTOR*)(base + dir.VirtualAddress); imp->Name; imp++)
    {
        for (auto thunk = (IMAGE_THUNK_DATA*)(base + imp->FirstThunk); thunk->u1.Function; thunk++)
        {
            for (auto& h : hooks)
            {
                if (!h.target || (void*)thunk->u1.Function != h.target) continue;
                DWORD old;
                VirtualProtect(&thunk->u1.Function, sizeof(void*), PAGE_READWRITE, &old);
                thunk->u1.Function = (ULONG_PTR)h.hook;
                VirtualProtect(&thunk->u1.Function, sizeof(void*), old, &old);
            }
        }
    }
}

static void Load()
{
    if (g_real) return;
    wchar_t self[MAX_PATH];
    HMODULE me = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCWSTR)&Load, &me);
    GetModuleFileNameW(me, self, MAX_PATH);
    wchar_t* slash = wcsrchr(self, L'\\');
    if (slash) slash[1] = 0;
    swprintf_s(g_dir, L"%sfmt-cursors\\", self);
    wchar_t path[MAX_PATH];
    swprintf_s(path, L"%son", g_dir);
    g_active = Exists(path);
    swprintf_s(path, L"%sfmt_dxvk.dll", self);
    g_real = LoadLibraryW(path);
    if (!g_real)
    {
        GetSystemDirectoryW(path, MAX_PATH);
        wcscat_s(path, L"\\d3d9.dll");
        g_real = LoadLibraryW(path);
    }
    HookImports();
}

extern "C" IDirect3D9* WINAPI FmtDirect3DCreate9(UINT sdk)
{
    Load();
    auto create = g_real ? (Create9Fn)GetProcAddress(g_real, "Direct3DCreate9") : nullptr;
    return create ? create(sdk) : nullptr;
}

extern "C" HRESULT WINAPI FmtDirect3DCreate9Ex(UINT sdk, IDirect3D9Ex** out)
{
    Load();
    auto create = g_real ? (Create9ExFn)GetProcAddress(g_real, "Direct3DCreate9Ex") : nullptr;
    return create ? create(sdk, out) : D3DERR_NOTAVAILABLE;
}

// Everything else is passed on unchanged.
#define FORWARD(name, ret, fail, params, args) \
    extern "C" ret WINAPI Fmt##name params { Load(); typedef ret (WINAPI* Fn) params; \
        auto fn = g_real ? (Fn)GetProcAddress(g_real, #name) : nullptr; return fn ? fn args : fail; }

FORWARD(D3DPERF_BeginEvent, int, -1, (D3DCOLOR c, LPCWSTR n), (c, n))
FORWARD(D3DPERF_EndEvent, int, -1, (), ())
FORWARD(D3DPERF_GetStatus, DWORD, 0, (), ())
FORWARD(D3DPERF_QueryRepeatFrame, BOOL, FALSE, (), ())
FORWARD(D3DPERF_SetOptions, int, 0, (DWORD o), (o))
FORWARD(Direct3DShaderValidatorCreate9, void*, nullptr, (), ())

extern "C" void WINAPI FmtD3DPERF_SetMarker(D3DCOLOR c, LPCWSTR n)
{
    Load();
    typedef void (WINAPI* Fn)(D3DCOLOR, LPCWSTR);
    auto fn = g_real ? (Fn)GetProcAddress(g_real, "D3DPERF_SetMarker") : nullptr;
    if (fn) fn(c, n);
}

extern "C" void WINAPI FmtD3DPERF_SetRegion(D3DCOLOR c, LPCWSTR n)
{
    Load();
    typedef void (WINAPI* Fn)(D3DCOLOR, LPCWSTR);
    auto fn = g_real ? (Fn)GetProcAddress(g_real, "D3DPERF_SetRegion") : nullptr;
    if (fn) fn(c, n);
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) InitializeCriticalSection(&g_lock);
    return TRUE;
}
