@echo off
rem Builds the 32-bit cursor shim into src\FMT\Assets\fmt_cursor.dll (FMT ships it and installs it as d3d9.dll).
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvarsall.bat" x86 >nul || exit /b 1
cd /d "%~dp0"
cl /nologo /O2 /MT /LD /EHsc /W3 fmt_cursor.cpp /link /DEF:fmt_cursor.def /OUT:..\..\src\FMT\Assets\fmt_cursor.dll user32.lib gdi32.lib || exit /b 1
del /q fmt_cursor.obj fmt_cursor.exp fmt_cursor.lib 2>nul
