# Áudio e estabilidade — 2026-10-02

- Configurações → AssistiveTouch automático → **Reconectar áudio** libera somente a referência A2DP deste aplicativo, aguarda a limpeza e tenta abrir novamente. Não altera drivers, emparelhamentos nem o serviço BLE de teclado/mouse. A automação do iPhone pode desligar e ligar o AssistiveTouch durante a reconexão.
- Quedas confirmadas são verificadas a cada segundo, com confirmação em uma segunda leitura. As novas tentativas usam intervalos de 2, 5, 15, 30 e até 60 segundos para não ficar reconectando sem parar. Ao retomar da suspensão do Windows, uma sessão de áudio habilitada é reaberta.
- O estado `Opened` da API não comprova que o som está audível. Silêncio não dispara reinicializações automáticas: pode ser uma pausa normal, saída selecionada no iPhone ou mixer do Windows. O botão de recuperação serve também para esse caso.
- A fila de teclado é processada em lotes de até oito itens, preservando sua ordem e dando oportunidade ao envio do mouse. Consultas de nomes Bluetooth saíram do caminho de troca de alvo.
- Envios HID acima de 100 ms geram um aviso técnico, no máximo uma vez a cada dez segundos. Esse aviso não inclui teclas, texto digitado ou coordenadas. A demora medida é da chamada ao Windows, não uma medição ponta a ponta do Bluetooth.

Validação: compilação Release e publicação win-x64 sem erros, 210 testes Core aprovados e smoke test das quatro páginas WPF aprovado, preservando preferências. Isso não comprova a eliminação de interferência ou de todas as falhas do rádio. Validar no iPhone reproduzindo áudio enquanto movimenta o mouse, além de reconectar áudio e retomar da suspensão. Não foram modificados drivers, firmware ou parâmetros do rádio.

As entradas Bluetooth Classic (áudio) e LE (controle) continuam separadas. O desenho do cursor continua sob controle do iOS; este aplicativo envia movimento e botões, não desenha o ponteiro na tela do iPhone.
