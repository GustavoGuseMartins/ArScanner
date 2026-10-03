# Recuperação do fluxo de localização e da câmera térmica

## Evidência dos últimos testes

Foram recolhidos 12 CSVs do aplicativo, preservando os arquivos do celular. As cinco sessões AR principais ocorreram entre 22:10 e 22:41 de 30/09/2026 (UTC−3); os nomes dos arquivos usam UTC, portanto começam com `20261001`.

Nos três primeiros ensaios, o estimador encontrou soluções multivista, mas nenhuma posição foi confirmada. A confirmação adicional chegou a três observações, porém apenas uma vista. Nas duas sessões seguintes apareceram posições em parte dos registros, mas nenhuma sessão confirmou yaw ou liberou a captura. Esses números contam estados registrados, incluindo repetições de uma mesma medida; não equivalem a observações independentes.

O problema está no fluxo introduzido na última versão:

- O apoio observado deixava de estar disponível após dois segundos fora do enquadramento antes da confirmação da pose. A solução com altura exigia esse apoio; a alternativa 3D também era bloqueada porque já havia uma altura armazenada.
- A confirmação exigia novamente múltiplas vistas após um cálculo já validado por geometria multivista. Ela só avançava quando uma amostra nova entrava no estimador. Ao atingir a quota de amostras próximas (`position_full`), pausar na última posição não concluía a confirmação.
- O reconhecimento experimental da tampa participava do caminho automático de direção, embora não tivesse sido validado em uso real. O zero salvo do motor informa o ângulo relativo da cabeça; ele não informa a orientação da base no mundo AR.

Arquivos de evidência: `diagnostics/20260930/regression/analysis.json`, `transitions.json` e os CSVs originais nessa pasta.

## Fluxo corrigido

1. Conectar o scanner por Wi-Fi e abrir o AR. A base USB e o perfil UWB habilitam a localização automática; sua ausência permite marcar o eixo manualmente no apoio.
2. No modo UWB, observar o apoio e percorrer posições distintas com o celular. Preservar o apoio confirmado enquanto scanner e sessão AR permanecerem na mesma referência. Após validar a geometria, três ciclos novos e completos de distâncias, compatíveis com a posição candidata e separados no tempo, confirmam sua estabilidade. Repetir o mesmo ciclo não conta como evidência nova.
3. Tocar em **Fixar eixo UWB estimado** para congelar a origem. Para a alternativa manual, usar **Marcar eixo no apoio pelo AR**, mirando a projeção do eixo no plano horizontal da mesa ou chão; o corpo e o topo do scanner não são o apoio. Essa marcação também fixa a posição explicitamente. A direção permanece bloqueada até a fixação.
4. Com a cabeça parada e seu zero conhecido, apontar a cruz para um ponto livre do apoio, 30–50 cm à frente do eixo, na direção física da frente do LiDAR, e tocar em **Definir direção no AR**. O ajuste desconta o pan e considera o deslocamento da tag; não depende do yaw anterior. Um zero do pan salvo não substitui essa direção no mundo AR.
5. Iniciar ou retomar a captura. O aplicativo espera a confirmação do scanner. Mudança da referência AR, perda do zero ou pose incompatível continuam exigindo nova validação. Se mover a base física do scanner após a fixação, parar a captura, limpar a nuvem e refazer posição e direção.

Reconhecimento da tampa e ajuste automático por ponto do apoio ficam desligados por padrão, em experimentos. As instruções vagas sobre enquadrar a tampa saem do fluxo normal. Ajustes de montagem, bancada, transporte, câmeras auxiliares e experimentos ficam no diagnóstico; controles de uso permanecem visíveis. A auditoria completa está no documento de funções do aplicativo.

## Câmera térmica

A biblioteca anterior fazia duas leituras, mas não garantia que fossem subpáginas diferentes. Nossa inicialização da imagem com NaN fazia uma repetição da mesma metade parecer falha de toda a câmera, acionando reinicializações. Além disso, a espera interna por dados prontos não tinha prazo total: o timeout de uma transação I2C não limitava esse laço.

O firmware passa a adquirir uma subpágina por tentativa, com leitura limitada no tempo, e montar explicitamente as metades 0 e 1. A validação física encontrou ainda uma configuração que preservava repetição/seleção de subpágina: a câmera entregava somente a metade 1. A inicialização agora habilita alternância contínua e verifica esse controle durante a captura. Subpágina repetida e espera normal não equivalem a imagem inválida; quadros antigos expiram.

As leituras completas em dois clocks, blocos diferentes e endereços isolados confirmaram 18 palavras de pixel sem calibração utilizável na EEPROM do sensor. O modo parcial exclui esses pixels antes da extração das escalas e do cálculo; preserva os coeficientes dos 750 restantes, não preenche temperaturas ausentes e impede sua fusão com pontos da nuvem. A prévia exibe lacunas e um aviso claro. Isso recupera informação disponível, sem reparar a calibração faltante nem comprovar exatidão de temperatura.

Usar **Ver imagem térmica** para abrir a prévia. Com este sensor, o painel informa **Imagem parcial: 18 pixels sem calibração foram excluídos** e mostra as lacunas transparentes sobre fundo preto. Um quadro recente no resumo do HUD significa que as duas subpáginas foram recebidas e todos os pixels válidos estão disponíveis; não significa que os 768 pixels tenham calibração.

O diagnóstico v10 distingue inicialização, quadro disponível, montagem, repetição, timeout, erro I2C, imagem inválida e calibração parcial. O aplicativo grava esses estados em `ScannerDiagnostics/Scanner_*.jsonl`, mesmo sem medidas UWB. O endpoint legado de 776 bytes continua disponível para o caminho normal; a prévia parcial usa `/thermal/masked`, com 872 bytes e máscara explícita. O aplicativo anterior recebe indisponibilidade no caminho parcial para não confundir lacunas com temperaturas. A alimentação foi verificada pelo usuário nos ensaios anteriores; a condição diferente da sessão de 02/10 está registrada abaixo.

## Limites de validação

Validação concluída em 01/10/2026: a suíte Unity passou, incluindo apoio observado há 100 segundos antes da primeira pose e fixação seguida do `Update` normal com distâncias divergentes; menu e visualizador passaram em Play Mode; testes nativos da aquisição térmica e compilação ESP32-S3 passaram. O APK de 36.418.677 bytes foi gerado e sua assinatura conferida, igual à versão anterior.

Artefatos da validação de 01/10: `Builds/ScannerAR-thermal-partial-20261001.apk` e `Builds/ScannerESP32S3-thermal-partial-alternating-20261001.bin`. Esse firmware foi gravado e seu hash verificado. Em 32 segundos de observação, publicou 99 quadros, cerca de quatro por segundo, com zero duplicatas, timeouts, erros I2C ou overruns. O zero do pan foi restaurado (`passos=0`); apenas a aplicação foi gravada, preservando a NVS e sem comandar motores. Uma imagem de diagnóstico com 872 bytes passou no CRC e excluiu exatamente os mesmos 18 pixels identificados na EEPROM. Esse resultado físico é histórico dessa versão; não valida o firmware de inicialização instalado em 02/10. A precisão absoluta de temperatura e a fusão durante movimento continuam sem validação física.

O APK final v10 com máscara foi instalado no Samsung SM-S916B (`RQCWB01MB7L`) por atualização, sem desinstalação. A instalação retornou `Success`, registrada em `diagnostics/20260930/thermal-partial-apk-install.log`; o APK tem o mesmo certificado da versão anterior. Os registros anteriores continuam presentes, incluindo CSVs de `UwbDiagnostics` e arquivos PLY de scans. O lançamento a frio retornou `Status: ok` em 588 ms; o processo 6892 permaneceu ativo e nenhum crash fatal foi observado no log desta execução, `diagnostics/20260930/thermal-partial-installed-startup.log`.

A tela segura do celular estava bloqueada (`showing=true`, `secure=true`), impedindo a conferência visual do aplicativo; não foi vista sua tela de uso. A rede `ArScanner_Net` não apareceu nessa verificação, portanto a integração HTTP entre o aplicativo e o scanner não foi conferida nesta instalação. Isso não altera a validação física anterior do firmware, que publicou 99 quadros com 750 pixels válidos e 18 excluídos. O firmware da base/visualizador continua compatível e não exige atualização para esta correção.

## Sessão de 02/10/2026: recuperação do transporte na inicialização

Ao reconectar o scanner à COM6, o v10 apresentou falha de leitura da EEPROM (`erro=-103`, `raw=-1`) e falhas persistentes de leitura da IMU. O zero do pan foi restaurado em zero passos. Foi encontrada uma lacuna de software: a redução do clock I2C para erros de transporte existia durante a aquisição, mas não quando a inicialização falhava.

`ThermalSensor::begin` agora permite no máximo duas tentativas por chamada, de 400 kHz para 100 kHz, somente para falhas de transporte identificadas pelo erro de inicialização e seu erro bruto. O clock é configurado antes da sondagem do sensor, os erros de leitura são contados também na inicialização e as tentativas seguintes já usam 100 kHz e quatro subpáginas por segundo, equivalentes a até dois quadros por segundo. Calibração inválida não dispara esse fallback; as exigências de EEPROM estável, máscara e validade dos pixels permanecem iguais.

A suíte nativa passou com `g++ -Wall -Wextra -Werror`, incluindo o dump da EEPROM física e os casos de fallback (`diagnostics/20260930/thermal-startup-native-validation-20261002.log`). A compilação PlatformIO passou com 181.772 bytes de RAM e 871.817 bytes de flash. O novo artefato `Builds/ScannerESP32S3-thermal-startup-recovery-20261002.bin` tem 872.176 bytes e SHA-256 `a00fa60d40ce4f776cac6eedc62516e22ac42eb2452bba5ce33edbe9b9a8f68e`. Foi gravado somente em `0x10000`, com hash verificado e NVS preservada, sem escrever EEPROM ou comandar motores. Registro: `diagnostics/20260930/thermal-startup-recovery-upload-20261002.log`.

A captura de 32 segundos após a gravação confirmou a execução do fallback, mas publicou zero quadros; os erros I2C aumentaram de 4 para 14 (`diagnostics/20260930/thermal-startup-recovery-real-boot-20261002.log`). O usuário confirmou que estava conectado somente o USB, com a alimentação principal desligada, e que não consegue ligá-la agora. Esse ensaio não comprova recuperação física do novo firmware nem uma regressão completa; não estabelece uma causa elétrica para os erros. A verificação física com alimentação principal ligada permanece pendente. A próxima checagem deve usar alimentação principal ligada e USB conectado. O APK v10 instalado continua compatível e não requer novo build ou instalação.

Estabilidade entre estimativas não comprova precisão absoluta de 10 cm. A correção de yaw por indicação do usuário também precisa de comparação física para comprovar 10°. Resultados de compilação, instalação e testes estão registrados em `diagnostics/20260930/recovery-delivery.json`.
