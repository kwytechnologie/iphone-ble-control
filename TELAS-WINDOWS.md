# iPhone nas telas do Windows — experimental

O aplicativo pode usar um monitor virtual como referência para a passagem do mouse ao iPhone. A posição é organizada em **Configurações do Windows → Sistema → Tela**, junto aos demais monitores.

**Não é transmissão de vídeo.** O iPhone continua mostrando seus próprios aplicativos; janelas, fotos e arquivos do Windows não são transferidos. Não unifica as conexões Bluetooth nem modifica o driver Bluetooth.

## Estado de validação

Em 03/10/2026, no notebook Windows 11 e iPhone usados no desenvolvimento, foram confirmadas a criação da tela virtual, a passagem pela borda direita e a atualização automática da posição após aplicar a disposição no Windows. O usuário confirmou que a atualização funcionou sem desativar e reativar o modo. Isso não garante compatibilidade com outros equipamentos.

O erro inicial `0x8007007E` foi reproduzido ao tentar resolver o módulo do callback gerenciado: o thunk JIT do .NET não pertence a um módulo PE, mas `SwDeviceCreate` tenta reter esse módulo. Uma ponte nativa pequena (`BleHid.NativeCallbacks.dll`) fornece o callback compatível, sem trocar drivers. Os quatro testes da ponte em .NET 8 passaram, incluindo encaminhamento dos dados, GC e chamadas concorrentes. Reparos gerais do Windows não resolveram esse erro do callback; a correção está no componente do aplicativo.

Passaram 238 testes do Core (sem executar o teste de hooks reais), incluindo oito testes de retomada/cancelamento; o teste WPF carregou as quatro páginas e verificou a rolagem proporcional sem alterar as preferências. Todos os lados, suspensão e diferentes topologias ainda devem ser validados separadamente. Assinatura digital válida do driver não garante compatibilidade em todo hardware.

## Compilar o pacote

Primeiro compile a ponte nativa x64 seguindo `tools/NativeCallbacks/README.md`. Com .NET 8, publique os dois projetos na mesma pasta:

```powershell
dotnet publish src/BleHid.App/BleHid.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/tela-windows
dotnet publish tools/VirtualDisplayHost/VirtualDisplayHost.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/tela-windows
```

O teste `BleHid.App.exe --smoke-test` verifica somente a interface, sem criar monitor, iniciar Bluetooth nem capturar entrada. Os dois executáveis não devem ser elevados juntos: a elevação do helper ocorre somente quando se solicita a tela.

Publique também `BleHid.NativeCallbacks.dll` junto ao helper; o projeto a copia automaticamente e a mantém fora do executável single-file. Não coloque essa DLL em System32. O teste `dotnet test tests/BleHid.VirtualDisplayHost.Tests` valida a ponte sem criar dispositivos nem solicitar UAC.

## Preparação do driver

Driver externo: [Virtual Display Driver 24.12.24](https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/tag/24.12.24), pacote oficial `Signed-Driver-v24.12.24-x64.zip`. A instalação local utiliza `tools/VirtualDisplay/Install-VirtualDisplay.ps1`, com hashes fixos do INF, catálogo e DLL, além da validação de assinatura digital. O pacote não é modificado.

O script requer confirmação de administrador, cadastra o pacote com `pnputil /add-driver` e cria uma configuração dedicada em `C:\VirtualDisplayDriver` para um único monitor 440×956 a 60 Hz. Se essa pasta já existir, para sem substituí-la. Não importa certificados, não desativa proteções, não ativa assinatura de teste e não cria um monitor permanente. Não é um instalador genérico distribuível: espera o pacote verificado em `.tools/virtual-display-24.12.24/package` do projeto.

Neste notebook, a política padrão impediu executar o script. Sem alterá-la, o pacote foi novamente verificado e cadastrado diretamente pelo `pnputil.exe` do Windows com confirmação UAC, enquanto a configuração dedicada foi copiada sem elevação. Não use `ExecutionPolicy Bypass` nem mude a política global para instalar este recurso.

O helper `BleHid.VirtualDisplayHost.exe` deve ficar ao lado de `BleHid.App.exe`. Criar a tela também requer UAC, pois [SwDeviceCreate exige administrador](https://learn.microsoft.com/en-us/windows/win32/api/swdevice/nf-swdevice-swdevicecreate). Apenas o helper é elevado; o aplicativo e seus controles continuam no nível normal do usuário.

## Uso

1. Na página **Controle**, clique em **Mostrar iPhone nas telas** e confirme a permissão do Windows.
2. Clique em **Organizar no Windows**. Use **Estender** e mantenha uma tela física como principal. A tela virtual pode aparecer com o nome do driver ou como monitor genérico, não necessariamente com o nome comercial do iPhone.
3. Arraste o retângulo virtual para encostá-lo a um monitor do PC e aplique. Não deixe um espaço nem apenas contato por um canto.
4. Volte ao aplicativo, selecione o iPhone conectado e ative o **modo telas**. Atravessar a borda compartilhada inicia o controle sem a espera de 0,35 s do modo tradicional.
5. Ctrl+Alt+Q devolve o controle ao PC. As opções de rodinha e retorno estimado continuam disponíveis; o retorno por movimento ainda é uma estimativa, pois o iOS não informa a posição real do cursor.

Com **Usar a posição definida nas telas do Windows** marcado e o modo telas já ativo, basta arrastar o retângulo e clicar em **Aplicar**: o aplicativo libera o controle por um breve instante, lê a nova posição e retoma a passagem automaticamente. Não é necessário desativar e ativar. **Organizar no Windows** permanece disponível durante o uso. A atualização também confere as posições a cada segundo, caso o Windows não envie a notificação.

A retomada só acontece se o modo já estava ativo e a tela virtual e o iPhone continuam disponíveis. Desativar, sair ou remover a tela cancela a retomada. Falhas mantêm o controle no PC. No modo tradicional (sem posição do Windows), alterações das telas continuam exigindo reativação manual. A criação da tela não ativa a captura automaticamente; a tela também não é criada automaticamente ao abrir o aplicativo.

## Limitações e recuperação

- Windows considera esse retângulo uma tela real. Arrastar uma janela para lá não a mostra no iPhone: ela fica invisível. Durante arrastes/teclas pressionadas não ocorre a troca de controle.
- **Recuperar janelas** move janelas normais, inteiramente fora das telas físicas, de volta ao PC. Janelas minimizadas, maximizadas ou elevadas podem exigir recuperação manual. **Remover tela virtual** encerra o componente e pede ao Windows para remover o monitor.
- **Sair** encerra o helper; fechar para a bandeja/minimizar mantém o app e a tela. Se o app morrer, o helper detecta o encerramento do processo pai e libera o dispositivo. A remoção pelo Windows é assíncrona.
- Em caso de janela perdida: remova a tela; alternativamente selecione a janela com Alt+Tab e use Win+Shift+seta para mover entre monitores.
- Para voltar ao comportamento anterior, remova a tela virtual e desmarque **Usar a posição definida nas telas do Windows**.
- O pacote do driver permanece no Driver Store após remover a tela. Desinstalação completa exige identificar o `oemNN.inf` correspondente exatamente ao `MttVDD.inf` desta instalação, confirmar que nenhum outro app usa o pacote, e remover apenas esse pacote. Não use exclusões por nome genérico nem remova drivers de GPU/spacedesk/Bluetooth. A pasta de configuração pode ser preservada como backup.
- O projeto externo alerta sobre conflitos em grandes atualizações de GPU/chipset. Remova a tela e avalie desinstalar o pacote antes dessas atualizações. Não deixe esta tela como principal.

## Validação física pendente

Após a instalação: verificar que a tela aparece e pode ser reposicionada; cruzar e voltar em cada lado usado; verificar arraste, teclado e Ctrl+Alt+Q; remover a tela e confirmar que a tela principal/resolução física permaneceram; repetir após fechar/reabrir. Nenhum teste automatizado substitui essa verificação no notebook e iPhone reais.
