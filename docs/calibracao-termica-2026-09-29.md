# Calibração do lado da câmera térmica

O aplicativo agora permite corrigir a direção observada pela lente e o espelhamento horizontal sem modificar a placa e sem gravar novamente o firmware a cada tentativa. O ajuste fica salvo na memória do scanner. Sem perfil salvo, o scanner inicia no perfil `1` (lente voltada para `+X` da cabeça, imagem nativa girada 90° para a direita, sem espelhamento), em resposta ao relato de que o lado exibido estava oposto ao lado observado. O perfil `0` preserva a hipótese anterior (`-X`). As capturas existentes não contêm um alvo físico que confirme definitivamente qual perfil corresponde à montagem.

## Ensaio no equipamento

1. Pare a captura, abra **Térmica** no aplicativo e mantenha o scanner imóvel. Coloque um objeto pequeno e quente, seguro para manusear, a cerca de 0,8–1,5 m da lente. Evite outras fontes quentes no fundo.
2. Mova o objeto para esquerda, direita, cima e baixo do campo da lente. Confira cada posição na prévia. Se esquerda e direita estiverem trocadas, toque **Espelhar imagem térmica**. A imagem e a associação de temperatura aos pontos mudam juntas.
3. Posicione o objeto quente sobre uma superfície que o LiDAR também alcance, com o pan parado. Inicie uma curta varredura e confira se os pontos quentes aparecem **na mesma superfície e do mesmo lado físico**. Se aparecerem do lado oposto da cabeça, pare a captura e toque **Inverter lado da câmera térmica**. Cada mudança limpa a nuvem anterior para não misturar dois referenciais.
4. Repita o ensaio com o pan em pelo menos duas orientações conhecidas. Registre quatro posições do alvo em cada orientação (prévia, ponto colorido e objeto físico). Se a prévia estiver correta mas a nuvem continuar deslocada, meça novamente o offset da lente e confira o sinal/zero do LiDAR antes de mudar outros parâmetros.
5. Reinicie o scanner e confira o perfil em `/status` (`thermalOrientationProfile`) ou `/geometry`. Ele deve permanecer no valor escolhido. Perfis: `0` = lado original/normal, `1` = lado oposto/normal, `2` = lado original/espelhado, `3` = lado oposto/espelhado.

O firmware aceita `POST /thermal/orientation?profile=0..3` apenas com a captura parada. O `GET /thermal` mantém o mesmo quadro nativo de 776 bytes; o aplicativo aplica o espelhamento da prévia conforme o perfil publicado. O firmware usa o mesmo perfil ao projetar cada ponto LiDAR sobre a matriz térmica. A câmera RGB e o rádio UWB não são alterados.

**Aceite físico:** o alvo quente deve aparecer no mesmo quadrante da prévia e sobre os pontos da superfície aquecida, em pelo menos dois ângulos de pan. A compilação e os testes matemáticos verificam a consistência do código; a escolha do perfil correto ainda depende desse ensaio no scanner.
