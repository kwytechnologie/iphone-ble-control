# Retorno ao PC sem teclado

Em Controle → Voltar ao PC sem teclado, desative o controle para editar as opções:

- Clique da rodinha: ativado por padrão. Pressionar (não girar) retorna ao PC e
  mantém a sessão. O clique central não é enviado ao iPhone enquanto esta opção
  está ativa. No PC o clique central mantém sua função. A rolagem não muda.
- Borda estimada: desativada por padrão, experimental. Requer modo telas e usa
  a direção oposta à posição do iPhone. A distância virtual padrão é 600 unidades
  de movimento, ajustável de 100 a 3000. Cada entrada supõe o centro virtual;
  não há leitura nem calibração automática da posição real no iPhone.
- Aumente a distância se o retorno for precoce; diminua se exigir movimento demais.
  Há proteção de 700 ms após a entrada e margem de 40 unidades além da borda.
  A proteção afeta somente a decisão de retorno, não atrasa os movimentos enviados.
- Arrastes e teclas pressionadas inibem o retorno estimado. Toques, aceleração e
  a posição real inicial podem desalinhar o modelo: mantenha um resgate disponível.
- Touchpad: nenhum gesto foi alterado. Dois dedos continuam rolando. Um gesto
  já configurado para gerar clique central pode usar o retorno da rodinha.
- Ctrl+Alt+Q continua sendo o atalho de emergência.

Inclui a correção de temporização start-to-start do mouse. Nenhum filtro de
suavização ou pacote extra foi acrescentado. Ganho de fluidez e retorno físico
dependem de validação no aparelho; testes automatizados não medem latência no iOS.
