# Teclado e rolagem

## Melhorias

- A página Controle usa a área de rolagem da navegação, sem uma segunda área que bloqueie a rodinha.
- A rolagem local respeita a proporção dos deltas e a quantidade de linhas configurada no Windows.
  Com três linhas, delta -120 move 48 DIP e delta -30 move 12 DIP; quatro frações equivalem a um passo inteiro.
  Listas, caixas de edição e áreas de rolagem internas preservam seu comportamento.
- A rolagem remota acumula deltas menores que 120. O resto é limpo ao trocar de destino ou reiniciar a captura.
- **Inverter rolagem no iPhone**, na página Controle, vem ligado por padrão nesta versão e não afeta o PC.
  Pare a captura antes de alterar a opção e depois ative novamente.
- Suporte à tecla extra ISO/ABNT de contrabarra, teclas numéricas e operadores do teclado numérico,
  Num Lock e tecla de menu. Enter principal e Enter numérico são diferenciados.
- A soltura usa a identidade física da tecla: mudar Num Lock durante uma tecla pressionada não deixa
  o uso HID original preso no relatório do iPhone.
- AltGr+Q não dispara a parada de emergência. Use **Ctrl + Alt esquerdo + Q** para voltar ao PC.

Essas mudanças não alteram o descritor HID nem os emparelhamentos.

## Acentos: confira o layout do iPhone

O layout de teclado físico do iPhone precisa corresponder ao teclado usado no PC.
Selecionar apenas o idioma Português e QWERTY não garante a correspondência dos símbolos.
O relato de til produzir acento agudo ainda não foi confirmado como resolvido.

No layout Windows Português Brasil ABNT, a tecla de til corresponde a
`SC28 / VK_DE / HID34`, posição já enviada pelo app. Trocar apenas de virtual key
para scan code não muda esse relatório; não foi feita uma inversão arbitrária dessas teclas.

As teclas específicas ABNT C1/C2 continuam fora do limite `0x65` do descritor atual.
Ampliar o descritor é uma etapa separada porque o iPhone pode manter o descritor
em cache. Num Lock/Caps Lock não são sincronizados com os LEDs do iPhone.

## Testes sugeridos

1. Abra Controle em janela pequena e role sobre textos e cartões com roda e touchpad.
2. Repita com modo telas ligado e destino **Este PC**.
3. Com destino iPhone, a roda deve rolar apenas no iPhone; não rolar a janela do PC nessa situação é esperado.
4. Volte ao PC pelo atalho ou pelo clique da rodinha, se habilitado, e confira a rolagem local.
5. Em um campo de texto de teste no iPhone, confira til, acento agudo, circunflexo, cedilha,
   aspas, contrabarra/barra, colchetes, Shift e teclado numérico com Num Lock ligado e desligado.

A suíte inclui testes das regras de teclado e rolagem. O modo `--smoke-test` do
aplicativo verifica as páginas WPF, proporções de deltas e controles internos sem
iniciar Bluetooth nem captura. Esses testes não substituem a validação no iPhone.

Para a automação de acessibilidade por Bluetooth, consulte
[ASSISTIVETOUCH-AUTOMATICO.md](ASSISTIVETOUCH-AUTOMATICO.md).

- [Microsoft: entrada de teclado e HID](https://learn.microsoft.com/en-us/windows/win32/inputdev/about-keyboard-input)
- [Microsoft: WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel)
- [Apple: teclado físico e acentos](https://support.apple.com/pt-br/guide/iphone/iphe62573ac4/ios)
