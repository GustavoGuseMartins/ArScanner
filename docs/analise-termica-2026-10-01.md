# Recuperação da aquisição MLX90640

O relato original da regressão veio após testes com a alimentação já verificada. O boot observado pelo responsável pela coleta mostrava inicialização em 8 Hz, redução para 4 Hz/100 kHz e reinicializações repetidas. Isso não demonstra falta de alimentação. A sessão posterior de 02/10, realizada somente com USB e alimentação principal desligada, está distinguida no final deste documento.

## Causa encontrada no código

A dependência instalada era Adafruit MLX90640 **1.1.2**, resolvida pela declaração `^1.0.2`. `getFrame` fazia exatamente duas chamadas a `MLX90640_GetFrameData`, sem confirmar IDs distintos de subpágina. `MLX90640_CalculateTo` escreve somente os 384 pixels correspondentes ao ID recebido. Se as duas leituras retornam o mesmo ID, a limpeza do buffer para NaN feita pelo scanner deixa a outra metade inválida. O scanner classificava isso como erro `-101`, reduzia a velocidade I2C e, após cinco ocorrências, reinicializava o sensor. A captura em andamento era confundida com falha de quadro.

Além disso, `MLX90640_GetFrameData` aguardava o bit data-ready em um laço sem prazo. O timeout do Wire limitava uma transação, mas não o laço de transações bem-sucedidas sem dados novos. Como a térmica e a IMU compartilham a tarefa auxiliar, essa espera podia atrasar indefinidamente a IMU. O teste de alimentação não elimina esses problemas de software.

## Comportamento corrigido

- Cada tentativa consulta data-ready uma vez. Se não há dados, devolve o controle à tarefa auxiliar.
- A montagem usa os IDs reais 0 e 1. Uma metade repetida substitui somente sua própria metade e não completa o quadro.
- O par deve completar até 500 ms a 8 subpáginas/s, ou 900 ms a 4 subpáginas/s. Repetições não prorrogam o prazo. Metades vencidas, reset e mudanças de configuração descartam o buffer parcial.
- Um quadro só é publicado depois das duas metades e da correção de pixels defeituosos conhecidos na EEPROM, com todas as 768 temperaturas finitas. A publicação é protegida entre os núcleos.
- A imagem recebe o timestamp do ponto médio das duas aquisições. A prévia aceita imagens completas com idade máxima de 1.000 ms; a fusão conserva seu limite temporal existente.
- Transações Wire têm timeout de 25 ms. A aquisição inteira de uma subpágina tem prazo de I/O de 300 ms, com ultrapassagem limitada pela transação em curso. O cálculo de temperatura não aguarda hardware.
- Erros de I2C podem reduzir a taxa e o clock. Uma troca de subpágina durante a leitura RAM reduz a taxa do sensor, mantendo I2C rápido. Uma metade incompleta não aciona essas reduções.
- Sem quadro completo por cinco segundos, o dispositivo volta ao processo de inicialização da tarefa auxiliar, cujas tentativas são espaçadas por dois segundos.

## Diagnóstico v9 e v10

`/status` mantém os campos anteriores e acrescenta `thermalFrameReady`, `thermalState`, `thermalSubpageMask`, `thermalDuplicateSubpages`, `thermalFrameTimeouts`, `thermalReadErrors`, `thermalOverruns`, `thermalInvalidFrames`, `thermalLastSubpage`, `thermalRefreshHz`, `thermalI2cHz`, `thermalReadDurationMs`, `thermalFrameSpanMs` e `thermalRawError`. `thermalRefreshHz` conta subpáginas por segundo: 8 equivale a até quatro quadros completos por segundo. `thermalReadDurationMs` descreve a última tentativa que chegou à leitura de subpágina, não a consulta normal sem dados.

Estados: `not_initialized`, `waiting_subpage`, `assembling`, `ready`, `stale`, `i2c_error`, `frame_overrun`, `frame_timeout`, `invalid_calibration`, `invalid_configuration` e `invalid_frame`. Espera e montagem normal têm erro zero. `-101` passa a significar temperatura inválida em uma leitura/quadro completo, `-104` prazo de montagem ou recuperação vencido, `-105` calibração inválida, `-106` configuração inválida e `-107` prazo total de transferência vencido. Os erros de transporte originais ficam separados no diagnóstico.

O v10 acrescenta `thermalPartialCalibration`, `thermalMaskedPixels`, `thermalCalibrationWarning` e o estado `ready_partial`. O aviso de calibração original permanece separado do erro de aquisição: uma imagem parcial disponível pode ter aquisição sem erro e aviso `-4`.

A serial imprime uma linha limitada a cada cinco segundos com estado, validade, quadros, idade, máscara e contadores. Após gravação, recuperação real exige observar quadros aumentando e imagens completas frescas. Uma mensagem de inicialização sozinha não comprova captura.

## Proveniência e validação

O adaptador I2C é local e não modifica `.pio/libdeps`. O subconjunto de cálculo e calibração Melexis vem do `utility/MLX90640_API.cpp` da Adafruit 1.1.2 instalada (SHA-256 `4505ADD69A746B1A558F10B7236A30C3C86AFCCB4F93C4135435A042E7296F30`). Foram preservados os cálculos de temperatura e correção de pixels. A adaptação remove I/O, logs, código não utilizado, limita normalizações da EEPROM e torna conversões de sinal explícitas. Os avisos de licença Apache 2.0 e o texto integral estão em `firmware/scanner/include/thermal_mlx90640_math.LICENSE`.

`thermal_acquisition_test.cpp` usa o código real do sensor, adaptador e cálculo com um Wire simulado. Cobre metades repetidas, NaN parcial, expiração, publicação completa, timestamp, reset entre metades, wraparound, data-ready ausente, data-ready contínuo durante RAM, deadline total, leituras curtas, EEPROM inválida, correção de um pixel defeituoso, rejeição de pixels defeituosos adjacentes e recuperação após cinco segundos. Passou nativamente. A compilação ESP32-S3 é executada com diretório de build fora de `.pio`.

## EEPROM observada no scanner conectado

A primeira gravação do v9 foi verificada na COM6, ESP32-S3 MAC `14:c1:9f:2c:52:14`. O zero do pan foi restaurado (`passos=0`). A térmica não publicou quadros: a extração recusou a calibração com `raw=-4`.

O diagnóstico recolheu 832 palavras desde `0x2400`, em ordem big-endian. IDs: `0A2D/8F62/018E`; palavra de modelo `EE[10]=048D`, bit 6 zero, correspondente ao MLX90640 no [driver oficial Melexis](https://raw.githubusercontent.com/melexis-fir/mlx9064x-driver-py/master/mlx/hw_i2c_hal.py). CRC32 de comparação do host: `127F0F51` — não é um checksum de fábrica.

Há 18 palavras de pixel `FFFF`, nos índices de pixel `5,33,61,89,117,146,174,202,231,260,288,316,344,373,401,429,458,486`. O bit 0 marca pixels desviantes; a API oficial interrompe a contagem no quinto. O [datasheet MLX90640](https://www.melexis.com/-/media/files/documents/datasheets/mlx90640-datasheet-melexis.pdf) especifica até quatro pixels desviantes não adjacentes. Esses dados excedem essa condição. A palavra `EE[41]=FFFF`, fora da área de pixels, representa quatro correções de coluna de −1 e não foi tratada como pixel inválido.

As leituras completas em 400 kHz e 100 kHz, em blocos de 32 e de 8 palavras, concordaram integralmente. Outras 57 leituras isoladas dos endereços `FFFF` e de seus vizinhos tiveram zero diferenças, erros, bytes ausentes ou negativos. O CRC antes e depois da extração também permaneceu igual. Isso confirma o conteúdo recebido repetidamente do sensor e afasta as hipóteses testadas de tamanho de bloco, endianness e mutação pela extração; não determina quando nem por que essas palavras passaram a ter esse conteúdo. Não houve escrita na EEPROM.

Evidências: `diagnostics/20260930/thermal-eeprom-chunks-real-boot.log`, `thermal-eeprom-real-boot-eeprom.json` e o binário bruto de 1.664 bytes preservado junto ao JSON. A extração offline do código real encontrou coeficientes finitos e sensibilidade positiva nos 750 pixels restantes (`thermal-coeff-offline-result.json`); isso não comprova sua exatidão térmica física.

## Recuperação parcial sem inventar calibração

O primeiro ensaio parcial ainda produziu zero imagens: o sensor repetia a subpágina 1. A configuração anterior ajustava apenas taxa, resolução e padrão chess, preservando os bits inferiores do controle `0x800D`. A palavra de configuração observada na EEPROM, `EE[12]=7F39`, contém a seleção/repetição de subpágina. A inicialização passa a habilitar subpáginas, desabilitar repetição e data hold e zerar o campo de seleção; os bits reservados são preservados. A alternância é verificada no controle e nos IDs reais das leituras. A exigência de duas metades distintas permanece.

Quando a EEPROM está estável em uma segunda leitura com blocos menores, o modelo é correto e todos os coeficientes globais e dos pixels elegíveis passam nas verificações, o firmware pode usar uma máscara explícita. O limite interno é de 38 pixels excluídos (menos de 5% de 768), preservando ao menos 730; é uma política de recuperação do projeto, não uma ampliação da especificação Melexis.

No modo parcial, os pixels marcados são excluídos antes das normalizações compartilhadas de Alpha/Kta/Kv e antes do cálculo de temperatura. Seus resultados ficam `NaN`. As listas Melexis de cinco posições não são usadas para interpolação. Uma imagem exige ambas as subpáginas dentro do prazo, todos os pixels elegíveis finitos e todos os excluídos ainda `NaN`. Uma amostra da nuvem recusa a temperatura se qualquer contribuinte bilinear de peso positivo estiver excluído; a geometria permanece disponível. Não há redistribuição de pesos para preencher a temperatura ausente.

`/thermal/masked` entrega 872 bytes: dois floats little-endian de mínimo/máximo dos pixels válidos, 768 intensidades e 96 bytes de máscara em ordem nativa (`row*32+col`, bit menos significativo primeiro, 1=válido). Escala, imagem e máscara pertencem ao mesmo snapshot. O aplicativo usa transparência, fundo preto, filtro `Point` e o aviso explícito de imagem parcial. O endpoint legado `/thermal` preserva 776 bytes para imagens completas dentro do caminho normal; responde 503 no modo parcial para evitar que aplicativos antigos exibam os pixels excluídos como temperaturas.

A precisão absoluta dos pixels restantes continua exigindo uma referência física de temperatura. A recuperação parcial não repara as palavras de calibração ausentes nem certifica o sensor na especificação original.

## Resultado físico histórico da gravação de 01/10

`ScannerESP32S3-thermal-partial-alternating-20261001.bin` foi gravado somente em `0x10000`, com hash verificado e NVS preservada. O boot final restaurou `pan=0` e registrou quadros `2,21,41,60,80,99` em intervalos de cinco segundos, todos frescos, com zero duplicatas, timeouts, erros I2C e overruns. O aviso `-4` e os 18 pixels excluídos continuam explícitos; a aquisição tem erro zero.

No binário diagnóstico, o controle foi lido como `7A01` e as subpáginas `1,0,1,0` foram observadas. O snapshot de 872 bytes passou no CRC32 `026872F9`, com 750 pixels elegíveis e a lista exata dos 18 excluídos. A transmissão serial do snapshot causou um único overrun, descartado; ele não se repetiu no binário final, que não emite esse dump. Evidências: `thermal-alternating-release-real-boot.log`, `thermal-alternating-diagnostic-real-boot-masked-frame.json` e `thermal-recovered-partial-frame.png`, em `diagnostics/20260930`.

A suíte nativa final passou com `-Wall -Wextra -Werror`: inclui a EEPROM física imutável, invariância dos coeficientes dos pixels bons ao mudar dados mascarados, controle alternante e bits reservados, modelo incorreto, calibração global inválida, releitura divergente, máscara, escala, bilinear e recuperação de metades repetidas. A suíte Unity e o build do APK com prévia mascarada também passaram. Precisão absoluta, calibração física e fusão durante movimento não foram certificadas por esses testes.

## Sessão de 02/10: fallback de transporte antes da aquisição

O scanner foi reconectado à COM6. A inicialização do v10 falhou na leitura da EEPROM com `erro=-103`, `raw=-1`; leituras da IMU também falharam persistentemente. O zero do pan foi restaurado em zero passos. Foi encontrada uma lacuna no software: o fallback para I2C mais lento só existia na aquisição, depois de uma inicialização bem-sucedida. Uma falha anterior de transporte não chegava a esse caminho.

A correção em `ThermalSensor::begin` limita cada chamada a duas tentativas, inicialmente a 400 kHz e depois a 100 kHz, e classifica transporte usando o erro de inicialização junto ao erro bruto. O clock passa a ser configurado antes da sondagem inicial; os contadores registram as falhas reais de leitura durante a inicialização. Após o fallback, a próxima chamada já permanece em 100 kHz e quatro subpáginas por segundo, equivalentes a até dois quadros por segundo. Rejeição de calibração não aciona a redução de transporte. O modelo, a estabilidade da EEPROM, os coeficientes globais, a máscara e a validação dos pixels conservam as mesmas exigências.

A suíte nativa passou com `g++ -Wall -Wextra -Werror`, usando o dump da EEPROM física e casos de falha/recuperação no fallback. Evidência: `diagnostics/20260930/thermal-startup-native-validation-20261002.log`. PlatformIO compilou com 181.772 bytes de RAM e 871.817 bytes de flash. O binário `Builds/ScannerESP32S3-thermal-startup-recovery-20261002.bin` tem 872.176 bytes e SHA-256 `a00fa60d40ce4f776cac6eedc62516e22ac42eb2452bba5ce33edbe9b9a8f68e`. Foi gravado somente em `0x10000`, com hash verificado e NVS preservada. Não houve escrita na EEPROM nem comandos de motores. Registro: `diagnostics/20260930/thermal-startup-recovery-upload-20261002.log`.

Durante 32 segundos após essa gravação, o fallback executou, mas o sensor publicou zero quadros e o contador de erros I2C passou de 4 para 14. Evidência: `diagnostics/20260930/thermal-startup-recovery-real-boot-20261002.log`. O usuário informou que estava usando somente USB, com a alimentação principal desligada, e que não consegue ligá-la agora. A condição observada não estabelece causa elétrica e não permite classificar o ensaio como recuperação física comprovada ou regressão completa. Os 99 quadros com 750 pixels válidos e 18 excluídos continuam sendo a validação histórica do firmware de 01/10; não são resultado do novo binário de 02/10.

Permanece pendente verificar o novo firmware com alimentação principal ligada e USB conectado. O APK v10 já instalado é compatível com essa correção e não precisa ser recompilado ou reinstalado.
