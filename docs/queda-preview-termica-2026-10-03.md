# Prévia térmica, pixels excluídos e GY-25 — 03/10/2026

A prévia térmica voltou a funcionar no aplicativo após a correção e gravação do firmware. O ensaio físico recebeu 100 imagens válidas, com 100 payloads distintos, sem erro HTTP nem reinicialização detectada. Os quadrados cinza da foto com a mão correspondem exatamente aos 18 pixels já excluídos pela máscara de calibração. Não foi alterada a calibração manual de posição ou direção.

## Queda ao abrir a câmera

Os registros do telefone mostram falhas de comunicação e redução do tempo ligado/contador de quadros do scanner nas reconexões da sessão `Scanner_20261003_174306_988.jsonl`. Há reinicializações reais do scanner nessa sessão. Os horários locais das falhas são aproximadamente 14:44:28 e 14:47:54. A sessão seguinte termina com nova falha às 14:52:49, sem uma reconexão registrada. O Android também registra desconexões Wi-Fi em 14:47:53 e 14:52:49. Não há registro suficiente para atribuir um horário exato ao toque no botão da câmera.

O binário anterior contém uma falha concreta no caminho HTTP: a tarefa `CameraHttpCore0` dispõe de 8.192 bytes de pilha; sua função usa 5.216 bytes e chama `ThermalSensor::getNormalizedFrame`, que usa outros 3.248. Só esses dois quadros exigem 8.464 bytes, antes das demais chamadas. O buffer local de 4.096 bytes de `/status` permanece no quadro da função mesmo quando ela atende `/thermal/masked`.

Em `firmware/scanner/src/main.cpp`, esse buffer passou a ser estático, com propriedade exclusiva da única tarefa HTTP. O quadro compilado da função HTTP caiu para 2.144 bytes. A soma dos dois quadros conhecidos passou a 5.392 bytes; a redução é de 3.072 bytes. A diferença até 8.192 precisa acomodar as demais chamadas e contexto: esta comparação não mede o máximo de pilha em execução.

O build ESP32-S3 passou e foi gravado na COM6, com verificação dos hashes pela ferramenta de gravação. O firmware de diagnóstico continua identificado como v12. Não foi instalado outro APK para esta correção.

## Validação física após a gravação

O teste final usou somente `GET /status` e `GET /thermal/masked`, através do Wi-Fi do telefone. Nenhum comando de movimento foi enviado.

| Observação | Resultado |
| --- | --- |
| Período local | 15:35:46–15:36:23 |
| Imagens solicitadas/recebidas | 100/100 |
| Payloads distintos | 100 |
| Respostas de estado | 20 |
| Erros HTTP / resets detectados | 0 / 0 |
| Pixels válidos / excluídos em cada imagem | 750 / 18 |
| Erros de leitura térmica / timeouts | 0 / 0 |
| Maior idade térmica nos estados amostrados | 353 ms |
| GY-25 pronta nos estados amostrados | 20/20 |
| Erros de leitura IMU / lacunas de integração | 0 / 0 |
| Maior idade da leitura IMU amostrada | 57 ms |

O usuário confirmou que a imagem aparece no aplicativo. A troca posterior de rede no telefone foi identificada pelo próprio usuário como um erro do celular e resolvida. O ensaio final demonstra estabilidade durante esse período com o scanner parado; não certifica uma captura longa com motor em movimento.

Os arquivos anteriores `thermal-probe-*` documentam a preparação do transporte binário. Algumas tentativas usaram saída de terminal com formatação ou conversão de bytes e não constituem testes válidos do payload. `thermal-stress-validated` ocorreu com o telefone fora da rede do scanner. O resultado físico usado acima é exclusivamente `thermal-stress-final`, com transferência binária por arquivo.

## Quadrados cinza na imagem da mão

A foto `Screenshot_20261003_153403_ScannerAR.jpg`, copiada da galeria do telefone, mostra a prévia 24 × 32 e a indicação de 18 pixels excluídos. Foram comparados os 768 centros das células da foto com a máscara do pacote físico e a transformação aplicada pelo aplicativo. No perfil térmico 1, a coordenada nativa `(linha, coluna)` corresponde a `(23-linha, coluna)` na tela, com origem no topo.

Os 18 centros cinza encontrados na foto coincidem com os 18 pixels da máscara, sem divergências. Os índices nativos são `5,33,61,89,117,146,174,202,231,260,288,316,344,373,401,429,458,486`. Por exemplo, os índices 260, 231 e 202 aparecem nas células de tela (15,4), (16,7) e (17,10). Os demais pixels da foto apresentam cor térmica.

![Posições dos pixels excluídos na imagem da mão](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/thermal-preview-investigation/thermal-mask-photo-overlay.png)

A análise reproduzível está em `thermal-mask-photo-analysis.json` e `analyze_thermal_mask_photo.py`, na pasta da investigação. O detalhamento consta de [auditoria-pixels-termicos-2026-10-03.md](C:/Users/aaata/Projetos/tcc/ArScanner/docs/auditoria-pixels-termicos-2026-10-03.md).

Essas exclusões já constam das leituras da EEPROM de 30/09. As palavras de calibração desses pixels são `FFFF`, com a indicação de parâmetro fora de especificação. Leituras repetidas, com outros tamanhos de bloco e clocks, concordaram. O [datasheet Melexis, página 21](https://www.melexis.com/-/media/files/documents/datasheets/mlx90640-datasheet-melexis.pdf#page=21) explica a indicação e especifica no máximo quatro pixels desviantes não adjacentes; os 18 observados excedem essa condição. O conteúdo recebido não permite determinar quando ou por que essa calibração passou a estar assim.

O aplicativo apresenta as células excluídas com transparência, deixando aparecer o fundo cinza do painel. A fusão térmica recusa uma temperatura quando algum pixel inválido contribui com peso positivo para a amostra. A geometria LiDAR permanece disponível. Não foi adicionada interpolação de temperatura nem escrita na EEPROM.

A imagem binária do teste final foi adquirida depois da foto, em outra cena, com faixa aproximada de 17–23 °C. Foi usada para comparar a máscara; não foi usada para validar a temperatura da mão, cuja foto mostra 18,7–33,2 °C. A identificação dos furos também não certifica a exatidão física dos 750 pixels restantes.

## GY-25 na nova medição

Nas duas sessões anteriores à correção, a IMU estava pronta em 442/442 estados HTTP válidos e recentes. Nos 97 intervalos de movimento elegíveis, o sentido de `gyroX` acompanhou o sentido do pan calculado pelos passos. Na sessão maior, a velocidade média pelo pan foi −11,976 °/s, pelo gyroX −11,643 °/s e pela projeção do gyro sobre a gravidade −11,874 °/s.

Esses registros têm aproximadamente um estado por segundo e o pan não é medido por encoder. A comparação confirma leitura e sentido de giro; não é uma certificação de escala ou precisão angular. A sessão maior acumulou 27 lacunas de integração. Os registros não devem ser integrados como uma orientação contínua.

Hoje o firmware lê o MPU6050 da GY-25 pelo I²C, calibra o bias inicial do gyro e publica leituras, ângulos de diagnóstico e saúde. `IMU_APPLY_TILT=false`: esses ângulos não alteram a nuvem ou o alinhamento AR. O campo legado `imuCalibrated=false` acompanha esse flag e não significa que o sensor esteja sem leitura ou sem calibração de bias. No ensaio final, `imuIdentity=104` (0x68), `imuBiasCalibrated=true`, `imuReadErrors=0` e `imuState=ready`, simultaneamente à aquisição térmica no mesmo barramento.

Há evidência para usar a GY-25 como observadora de movimento e giro relativo, mas ativar a compensação atual diretamente usaria um mapeamento provisório. O `angleZ` existente integra o eixo Z do sensor, enquanto o giro vertical desta montagem aparece principalmente no eixo X. Uma orientação relativa utilizável requer integração local, tratamento de lacunas/reset, referência explícita e compensação do giro do motor. O MPU6050 sozinho não fornece uma direção absoluta no AR nem posição X/Z. Nenhuma compensação foi ativada neste ensaio.

## Evidências e limitações de entrega

- `diagnostics/20261003/thermal-preview-investigation/thermal-stress-final.summary.json` e `.jsonl`: ensaio físico final.
- `diagnostics/20261003/thermal-preview-investigation/gyro-analysis.json`: critérios e análise das sessões de medição.
- `diagnostics/20261003/thermal-preview-stack-comparison.json`: comparação compilada anterior à gravação; seu campo histórico `firmwareFlashed=false` descreve aquele momento.
- `diagnostics/20261003/thermal-preview-firmware-upload.log`: entrega posterior, concluída.
- `diagnostics/20261003/thermal-preview-delivery.json`: manifesto de entrega e hashes.

A regressão térmica nativa compilou com avisos tratados como erros, mas a execução do executável foi bloqueada pelo Controle de Aplicativos do Windows. Não houve tentativa de contornar esse bloqueio. A execução nativa não é contada como aprovada; a validação física acima foi concluída separadamente.

O último estado registra `panReferenceValid=false` e `panReferenceState=interrupted`. Antes da próxima captura, o usuário precisa alinhar fisicamente a cabeça e confirmar o zero do pan pelo aplicativo. Não foi imposto um zero por software, nem comandado movimento durante a investigação.
