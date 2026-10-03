# Versão de referência antes da orientação pela GY-25

Esta versão preserva o comportamento aceito pelo usuário no teste de **03/10/2026, 16:12–16:14**, antes de experimentar compensação de orientação com a GY-25. A referência Git é a tag `scanner-baseline-20261003-1614`.

## Resultado aceito

O usuário reaproveitou o perfil UWB, ajustou o eixo rapidamente e estimou aproximadamente **10 cm** de diferença em relação ao eixo físico, considerada aceitável. Essa é uma estimativa visual do usuário, sem medida independente. A direção yaw foi alinhada manualmente.

Os registros mostram a fixação final aos 34,98 s desde a primeira linha da sessão AR, direção manual definida aos 43,44 s e captura confirmada aos 60,23 s. A posição registrada permaneceu constante da confirmação de captura até o fim do arquivo, por cerca de 99 s; isso inclui o período após a captura. A estabilidade do estado registrado não certifica ausência de deriva física do AR.

O vídeo amostrado mostra a fixação e correção da direção, o início da varredura e a formação da nuvem enquanto o usuário observa superfícies e objetos do ambiente. O vídeo tem aproximadamente 163,51 s; seu horário de criação é o final da gravação. A amostragem de cinco segundos não registra todos os toques nem mede o erro físico de posição.

## Configuração preservada

- Perfil UWB salvo em 02/10: escalas 0,769570 / 0,919901 / 0,820569 e offsets −0,266894 / −0,633963 / −0,347418 m. Não houve recalibração nessa tentativa.
- Yaw manual na última tentativa: 295,97°, fonte `manual_support`; pan de referência 0°.
- `IMU_APPLY_TILT=false`: a GY-25 lê e publica diagnóstico, mas não altera a orientação da nuvem.
- Térmica com 750 pixels elegíveis e 18 excluídos pela calibração, com aviso explícito de imagem parcial.
- Controle de opacidade dos pontos disponível. O descarte opcional de retornos LiDAR fracos fica desligado por padrão; não foi adicionado limite de distância.

## Aquisição observada

Na última sessão, 159/159 estados HTTP válidos e recentes confirmam térmica fresca e IMU pronta. A térmica produz aproximadamente 3,90 quadros completos/s, sem erro I²C, timeout térmico, overrun ou quadro inválido. O contador do firmware ganha 3.160 associações térmicas; são eventos de associação, não pontos únicos do aplicativo.

O gyroX acompanha o sentido do pan em 40/40 intervalos elegíveis. O pan é calculado pelos passos, sem encoder independente. Houve 21 novas lacunas de integração IMU e oito descartes por pose/tempo (`poseDrops`), sem causa conclusiva. Um timeout HTTP após a captura recuperou na resposta seguinte, com uptime crescente; não há reinicialização observada dentro dessa sessão.

Essas observações justificam preservar posição/UWB e investigar cadência, lacunas, eixos e referência relativa antes de ativar orientação pela GY-25. Os snapshots HTTP não são uma sequência adequada para integração angular contínua.

## Código, ferramentas e binários

O checkpoint inclui o projeto Unity, fontes/configurações dos dois firmwares, bibliotecas DW1000 locais, validações, ferramentas de bancada, documentação, netlists elétricas e modelos mecânicos Blender. Dependências geradas pela PlatformIO/Unity e registros brutos da galeria permanecem locais.

- Unity do projeto: 6000.5.4f1, conforme `ProjectSettings/ProjectVersion.txt`.
- Scanner: PlatformIO, ambiente `esp32s3`, ESP32-S3 com Arduino.
- Firmware gravado após a correção de pilha HTTP: diagnóstico v12, 873.920 bytes, SHA-256 `42c5220e686f8eedbd2e3f0824cb74abe6240ffac5eb5c396bf9e6d785ec09b7`.
- O aplicativo usado nesse teste é o instalado em 02/10, às 23:46:47. Não foi instalado outro APK durante a investigação de 03/10. O APK preparado em 03/10 para diagnóstico de retornos LiDAR fracos não foi instalado por esta sessão.
- O APK efetivamente instalado foi preservado localmente antes de qualquer novo experimento: 36.420.281 bytes, SHA-256 `5e699049f12fa9a534b7e6d57bc29bcbb7e1783de2ee2a1470f43a73ebc8111d`. A cópia está em `diagnostics/20261003/git-baseline-1614/ScannerAR-installed-best-test.apk`; esse binário local não integra o Git.
- A correção HTTP já gravada teve 100 imagens distintas recebidas em um ensaio parado, sem erros nem resets detectados. Esse ensaio é separado da captura melhor descrita acima.

Esta publicação registra a árvore atual do código e as validações existentes; não representa uma nova compilação/instalação do aplicativo ou uma mudança no scanner.

## Evidências locais preservadas

O vídeo original e os 11 arquivos de registros do aplicativo foram copiados para `diagnostics/20261003/best-test-1614`, com hash SHA-256 conferido contra o telefone. O manifesto identifica caminhos, tamanhos e hashes. A análise da localização e da saúde do scanner exclui linhas truncadas, snapshots obsoletos e callbacks repetidos conforme os critérios indicados nos respectivos JSONs.

As análises reproduzíveis dessa sessão estão em `localization-analysis.json`, `analyze_localization.py`, `scanner-health-analysis.json` e `analyze_scanner_health.py`, nessa pasta local. Os documentos [queda da prévia térmica](queda-preview-termica-2026-10-03.md), [montagem da GY-25](ensaio-montagem-gy25-2026-10-03.md) e [aproveitamento do hardware](aproveitamento-hardware-2026-10-03.md) registram os ensaios anteriores e seus limites.
