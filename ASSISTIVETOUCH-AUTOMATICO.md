# AssistiveTouch automático

## Configuração

1. Emparelhe o iPhone com o Bluetooth normal do PC, além da conexão BLE de teclado/mouse.
2. Em **Configurações → AssistiveTouch automático**, atualize a lista e escolha o iPhone.
3. Ative **Conectar junto com o aplicativo**. Essa opção vem desativada por padrão.
4. No Atalhos do iPhone, crie duas automações para a entrada **normal** do PC:
   conectar → ativar AssistiveTouch; desconectar → desativar AssistiveTouch.
5. Configure execução imediata e teste os dois eventos no aparelho.

Com **Iniciar o periférico ao abrir** ativado, a conexão auxiliar também inicia ao
abrir o aplicativo. Caso contrário, use **Iniciar** na página Início.

## Como funciona

O app usa `Windows.Media.Audio.AudioPlaybackConnection` para abrir o perfil de áudio
do Bluetooth normal do PC. O teclado/mouse continua usando o BLE existente.
Não há troca de driver, de descritor HID ou de emparelhamento. As duas entradas
Bluetooth não são mescladas. **O áudio do iPhone pode passar a sair no PC.**

**Parar**, desativar a opção ou **Sair** libera somente a referência de áudio do
aplicativo. Fechar a janela para a bandeja mantém o aplicativo e a conexão ativos.
Outros programas ou perfis conectados ao iPhone podem impedir o evento de
desconexão; o app não os encerra nem desliga o rádio inteiro.

Sem um dispositivo selecionado não há conexão. A escolha é salva nas preferências
locais, fora do repositório. Se o aparelho não aparecer, confira seu emparelhamento
normal para áudio; estar conectado apenas como teclado/mouse BLE não é suficiente.

Falhas de conexão usam esperas de 15, 30 e depois 60 segundos entre tentativas,
sem abrir conexões concorrentes. Falha ao liberar uma referência bloqueia novas
tentativas até reiniciar o aplicativo. O encerramento aguarda a limpeza por até
seis segundos, sem bloquear a interface.

## Validação e limites

A abertura e a liberação do perfil, junto com as automações de ativar e desativar
AssistiveTouch, foram confirmadas em um iPhone 16 Pro Max. Isso valida o caminho
na configuração testada, não garante compatibilidade universal.

O sucesso da API do Windows não prova que a automação foi executada no iPhone.
As automações precisam ser configuradas e verificadas no aparelho; o app não
altera diretamente a configuração de acessibilidade do iOS.

- [Microsoft: conexão de áudio Bluetooth](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/enable-remote-audio-playback)
- [Apple: acionadores de automações](https://support.apple.com/pt-br/guide/shortcuts/apde31e9638b/ios)
