# iPhone BLE Control

Controle o iPhone com o teclado e o mouse de um PC Windows por Bluetooth Low Energy.
Interface desktop, seleção do dispositivo, passagem pela borda do monitor e retorno ao PC.

**Projeto experimental e independente.** Não é um produto da Apple e não tem vínculo com ela.
O app envia comandos de teclado/mouse; **não transmite vídeo e não transforma o iPhone em um monitor do Windows**.

## Código aberto

Este projeto deriva de [windows-ble-hid, de Abhishek Raj](https://github.com/abhishek-raj/windows-ble-hid),
com base no commit `9a4f45129779ec3bef368ea4ffcdcde795a57b65`.
A [licença MIT](LICENSE) original foi preservada. O código desta distribuição pode ser estudado,
modificado e redistribuído nos termos dessa licença, mantendo os avisos exigidos.

Esta publicação é um snapshot do código adaptado; não contém o histórico Git completo do projeto original.
Veja [UPSTREAM.md](UPSTREAM.md) e [DEVELOPMENT.md](DEVELOPMENT.md) para a documentação técnica de origem.
As medições e compatibilidades relatadas nesses documentos pertencem ao upstream, não são uma nova
certificação deste fork. As instruções desta página prevalecem para a versão adaptada.

## Requisitos

- PC Windows com adaptador Bluetooth que suporte **LE Peripheral Role**.
- Windows 11 é o ambiente usado para validar esta adaptação. O projeto declara suporte mínimo
  a Windows 10 build 19041, mas essa combinação não foi validada novamente aqui.
- iPhone com AssistiveTouch ativado para exibir o ponteiro. Controle básico confirmado em um iPhone 16 Pro Max;
  outros modelos, drivers e versões podem se comportar de forma diferente.
- Para compilar: [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0) no Windows.

Não há um driver alternativo incluído. Um adaptador sem o modo periférico BLE não passa a ser compatível por instalar o app.

## Baixar e compilar

Use **Code → Download ZIP** nesta página ou clone o repositório:

```powershell
git clone https://github.com/kwytechnologie/iphone-ble-control.git
cd iphone-ble-control
dotnet restore src/BleHid.App/BleHid.App.csproj
dotnet build src/BleHid.App/BleHid.App.csproj -c Release
dotnet run --project src/BleHid.App/BleHid.App.csproj -c Release -- --screen-layout
```

Para gerar uma pasta que possa ser executada em outro PC Windows x64 sem instalar o .NET:

```powershell
dotnet publish src/BleHid.App/BleHid.App.csproj -c Release -r win-x64 --self-contained true -o publish/app
```

Abra `publish/app/BleHid.App.exe`. **Compartilhe a pasta inteira**, não só o executável.
Os binários compilados não têm assinatura digital. Verifique a procedência e o código antes de executá-los;
não desative o antivírus para usar o projeto.

O código da CLI também está disponível:

```powershell
dotnet build src/BleHid.Cli/BleHid.Cli.csproj -c Release
```

## Conectar o iPhone

1. Abra o app no PC e inicie o periférico Bluetooth.
2. No iPhone, abra **Ajustes → Acessibilidade → Toque → AssistiveTouch** e ative o recurso.
3. Em **Dispositivos → Dispositivos Bluetooth**, selecione o PC e conclua o emparelhamento.
4. Confira no aplicativo se teclado e mouse foram conectados antes de ativar o controle.

O Windows pode anunciar duas entradas com o mesmo nome (Bluetooth Classic e BLE).
Estar conectado na lista geral de Bluetooth não garante que o teclado e mouse BLE estejam ativos.
Os nomes dos menus podem variar com o idioma e a versão do sistema.

## Passar entre PC e iPhone

Na seção **Controle**, configure o monitor, o iPhone e sua posição: direita, esquerda, acima ou abaixo.
Ative o modo telas e leve o ponteiro à borda escolhida. Há uma pequena permanência na borda para evitar trocas acidentais.

Para voltar ao PC:

- **Ctrl + Alt esquerdo + Q:** atalho de emergência. Em modo telas, a sessão pode continuar armada; AltGr+Q não encerra o controle.
- **Clique da rodinha:** ativado por padrão; pressione o botão central. Girar a rodinha continua rolando.
- **Borda estimada:** opção experimental, desativada por padrão. O app estima o deslocamento, mas não recebe
  a posição real do ponteiro no iOS. Ajuste a distância se o retorno ocorrer cedo ou tarde.
- **Ctrl + D + C:** alterna os destinos disponíveis, incluindo este PC.

Pare o controle antes de alterar as opções. Gestos do touchpad não são remapeados;
um gesto já configurado no Windows para clique central pode servir como retorno.
Veja [RETORNO-SEM-TECLADO.md](RETORNO-SEM-TECLADO.md).

## AssistiveTouch automático

Em **Configurações → AssistiveTouch automático**, selecione o iPhone já emparelhado
para áudio e ative **Conectar junto com o aplicativo**. A opção vem desligada por padrão.
O app abre uma conexão de áudio pelo Bluetooth normal do PC, além do teclado/mouse BLE,
para acionar as automações de conectar/desconectar configuradas no Atalhos do iPhone.

**O áudio do iPhone pode sair no PC.** Não há troca de driver nem unificação das duas entradas.
**Parar**, desativar a opção ou **Sair** libera a conexão auxiliar. Fechar para a bandeja a mantém.
Outros aplicativos conectados podem impedir que o iPhone detecte a desconexão.
Veja a configuração e as limitações em [ASSISTIVETOUCH-AUTOMATICO.md](ASSISTIVETOUCH-AUTOMATICO.md).

## Teclado e rolagem

- A rolagem das páginas do app é proporcional aos gestos do touchpad e segue a quantidade de linhas do Windows.
- **Inverter rolagem no iPhone**, na página Controle, muda apenas o sentido da rolagem remota.
- Deltas pequenos de rolagem remota são acumulados, em vez de descartados.
- Melhorias para teclado numérico, tecla extra ISO/ABNT e soltura de teclas ao mudar Num Lock.

A correspondência de acentos ainda depende do layout de teclado físico selecionado no iPhone.
Veja [TECLADO-E-ROLAGEM.md](TECLADO-E-ROLAGEM.md) para os limites e testes sugeridos.

## Limites e cuidados

- O retorno por borda é aproximado. Aceleração do iOS, toques e posição inicial podem desalinhar a estimativa.
- O tempo de resposta depende do rádio, do driver e do intervalo BLE negociado. Não há promessa de latência zero.
- Reiniciar o app ou o Bluetooth pode exigir reconexão ou novo emparelhamento.
- Não execute simultaneamente duas instâncias do periférico (app e CLI).
- Enquanto a captura estiver ativa, teclas e cliques são enviados ao dispositivo selecionado.
  Confirme o destino antes de digitar dados confidenciais; evite o modo de transmissão para todos os hosts.
- A CLI inclui diagnósticos avançados e o modo `--plain`, que reduz a proteção de acesso dos relatórios HID.
  Não use esse modo para operação normal.

Configurações e logs ficam em `%LOCALAPPDATA%/BleHid`, fora do repositório. Revise qualquer log antes de compartilhá-lo:
diagnósticos podem conter nomes de dispositivos, identificadores e detalhes de entrada quando habilitados.

## Testes e contribuição

```powershell
dotnet test tests/BleHid.Core.Tests/BleHid.Core.Tests.csproj -c Release
git diff --check
```

A suíte automatizada cobre relatórios HID, temporização, bordas, teclado, rolagem e recuperação de controle.
Ela não comprova compatibilidade física nem mede a latência no iPhone.
`tests/Invoke-HandoverTests.ps1` é um teste manual com rádio real e encerra processos do app/CLI:
**não o execute durante uma sessão em uso**.

Para contribuir, faça um **fork**, crie uma branch e envie um **pull request**.
Veja [CONTRIBUTING.md](CONTRIBUTING.md). Colaboradores não precisam receber sua senha do GitHub.

## Estrutura

- `src/BleHid.App`: aplicativo WPF e ícone.
- `src/BleHid.Core`: Bluetooth, captura de entrada e regras de troca.
- `src/BleHid.Cli`: comandos e diagnóstico.
- `tests`: testes automatizados e cenários manuais.
- `tools`: utilitários de desenvolvimento.
- `android`, `experiments`, `spike`: código experimental herdado do upstream; não é necessário para controlar o iPhone.
