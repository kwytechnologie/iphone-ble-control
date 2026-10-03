# Callback nativo do monitor virtual (Windows x64)

`SwDeviceCreate` mantém uma referência ao módulo PE que contém seu callback.
No Windows 11 deste teste, `GetModuleHandleExW(FROM_ADDRESS, callback)` falha
com erro 126 quando recebe um thunk de delegate JIT do .NET, mesmo com todas
as DLLs instaladas. Isso produz `HRESULT 0x8007007E` antes da enumeração.

Esta DLL fornece somente um callback nativo, encaminhando os argumentos ao
delegate mantido vivo pelo helper. Não cria dispositivos, não instala drivers,
não captura entrada e não tem estado global. A DLL e o delegate precisam ficar
vivos até `SwDeviceClose` retornar. `UnmanagedCallersOnly` com JIT não substitui
esse requisito de código dentro de módulo PE.

## Build reproduzível

Na raiz do repositório, usando Zig 0.15.2 oficial no PATH (ou substituindo `zig` pelo caminho do executável portátil):

```powershell
New-Item -ItemType Directory -Force tools/NativeCallbacks/artifacts/win-x64
zig cc -target x86_64-windows-gnu -shared -O2 -Wall -Wextra -Werror '-Wl,--dynamicbase' '-Wl,--nxcompat' -o tools/NativeCallbacks/artifacts/win-x64/BleHid.NativeCallbacks.dll tools/NativeCallbacks/NativeCallbacks.c
```

Pacote local: `https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip`

SHA-256 publicado em `https://ziglang.org/download/index.json`:
`3a0ed1e8799a2f8ce2a6e6290a9ff22e6906f8227865911fb7ddedc3cc14cb0c`.
O compilador fica em `.tools`, não é instalado globalmente nem distribuído com o app.

Alternativamente, após criar a mesma pasta de saída, compile com MSVC no **x64 Native Tools Command Prompt**:

```text
cl /LD /O2 /W4 /WX /GS /Fo:tools/NativeCallbacks/artifacts/win-x64/NativeCallbacks.obj tools/NativeCallbacks/NativeCallbacks.c /link /DYNAMICBASE /NXCOMPAT /OUT:tools/NativeCallbacks/artifacts/win-x64/BleHid.NativeCallbacks.dll
```

Depois publique o helper normalmente. O projeto exige a DLL, copia-a para o
output e mantém o arquivo externo à publicação single-file. Ela deve ficar ao
lado de `BleHid.VirtualDisplayHost.exe`, nunca em System32.

## Teste sem dispositivos ou UAC

```powershell
dotnet test tests/BleHid.VirtualDisplayHost.Tests -c Release
```

Verifica módulo PE, encaminhamento de HRESULT/handle/texto Unicode/nulo,
delegate vivo após GC e independência de chamadas concorrentes. A criação e
remoção reais do monitor ainda devem ser verificadas separadamente no Windows.
