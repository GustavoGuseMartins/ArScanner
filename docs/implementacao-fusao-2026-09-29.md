# Implementação de 29/09: térmica, pontos e tempo UWB/AR

Esta versão usa ARCore do celular, alcances UWB, LiDAR, pan e térmica do scanner. A RGB do scanner permanece desabilitada pela pinagem atual. A fusão temporal foi inspirada por VIRAL/LIRO, mas não constitui VIRAL SLAM completo nem autoriza reconstrução com a base do scanner em movimento.

## O que entrou

- **Térmica:** o padrão sem preferência salva passa a testar lente voltada a `+X`, o lado oposto à hipótese anterior. Com o scan parado, o aplicativo pode inverter o lado ou espelhar a imagem. O perfil 0–3 fica salvo no ESP32-S3; prévia e associação temperatura/ponto usam o mesmo perfil. A troca limpa a nuvem anterior. O controle RGB fica desabilitado quando a câmera não está disponível.
- **Pontos:** diâmetro dos billboards reduzido de 25 mm para 6,25 mm no renderizador e na cena. A voxelização permanece em 25 mm. O LOD continua ligado e une superfícies densas; rejeita buracos amostrados de forma insuficiente e leituras de lados opostos, preservando pontos individuais nesses casos.
- **Posição:** para diagnósticos UWB v2 coerentes, o aplicativo estima a relação entre o relógio da base e o do celular pelo menor atraso USB observado. Os horários `t1/t2/t3` consultam um histórico AR contínuo e interpolam uma pose para cada antena. Pacotes atrasados, lacunas de rastreamento e saltos de relocalização não geram pose. O protocolo legado v1 só usa a pose no recebimento após o celular ficar imóvel.

## Validação executada

- Teste nativo de geometria térmica: passou.
- Compilação do firmware ESP32-S3: passou com diretório de build temporário.
- `ScannerPoseValidation.Run` em Unity 6000.5.4f1: passou. Inclui regressões do LOD, relógio/histórico AR, trilateração e multivista existentes.
- Build Android: passou. APK: `Builds/ScannerAR-fusao-termica-20260929.apk`.
- Firmware compilado e gravado em 30/09: `Builds/ScannerESP32S3-termica-20260930.bin`.

O APK foi **instalado no Samsung SM-S916B em 30/09/2026**, mantendo os dados do aplicativo. A tela inicial abriu, mostrou o perfil UWB salvo e não apresentou erro de inicialização nos registros verificados. Scanner e base UWB estavam desconectados nesse teste. O firmware foi **gravado no scanner ESP32-S3 em 30/09/2026**, com verificação dos dados escritos. Após reiniciar, o scanner confirmou perfil térmico 1, MLX90640 a 8 Hz e tag DWM1000 operacional; o registro está em `diagnostics/20260930/scanner-startup.log`. Também foi corrigido o carregamento das preferências na primeira inicialização, que antes emitia aviso por não existir um perfil salvo. Os testes verificam regras, gravação e inicialização; não medem erro físico de posicionamento nem confirmam o lado óptico da montagem.

## Leitura dos registros UWB existentes

O replay de 27/09 usa protocolo antigo sem horários individuais dos rádios. Suas 59 amostras foram rejeitadas pela geometria 3D; a captura não serve para ajustar os limites do estimador atual. No CSV mais recente de 29/09 (`diagnostics/20260928/latest-v2/Uwb_20260929_011645_042.csv`), houve **47 novas soluções multivista** com apoio observado. O resíduo mediano foi 7,6 cm e a dispersão interna estimada foi 1,7 cm; essa dispersão **não é o erro absoluto de posição**. O arquivo contém reinícios da coleta e pode incluir reposicionamentos físicos do scanner, então a variação global da pose também não mede erro. Mesmo com scanner marcado como parado, pan ocioso, ciclo coerente e celular quase imóvel, 452 de 3.043 ciclos (14,9%) tinham alcances incompatíveis entre si pela margem geométrica de 25 cm. Isso pode refletir multipercurso, obstrução ou movimento físico não registrado. Não foi encontrado bug demonstrável que justificasse afrouxar os critérios de aceitação.

## Próximo ensaio físico

1. Usar o APK e firmware desta versão; manter o scanner parado sobre apoio medido.
2. Confirmar o lado da térmica com um alvo quente em quadrantes conhecidos e em dois ângulos de pan, conforme [calibracao-termica-2026-09-29.md](calibracao-termica-2026-09-29.md). Se o perfil 1 estiver errado, usar o botão no aplicativo; a escolha fica salva no scanner.
3. Comparar o eixo virtual com o eixo físico em posições/alturas medidas e após mover o celular para três pontos de vista distintos. Guardar CSV UWB e prints na mesma distância para comparar nuvem, lacunas e LOD.
4. Medir erro horizontal, vertical e angular, tempo até pose aceita e leituras rejeitadas. Só definir meta de precisão e habilitar scanner móvel com esses dados.

A latência USB absoluta ainda não é conhecida: usar o menor atraso observado melhora o alinhamento relativo, mas deixa um viés temporal possível. O relógio dos pontos LiDAR é independente; falta sincronizá-lo e obter pose mundial contínua da base para transformar cada ponto durante movimento. A captura com scanner móvel permanece bloqueada. O visualizador acompanha o celular pelo ARCore com a base do scanner parada.
