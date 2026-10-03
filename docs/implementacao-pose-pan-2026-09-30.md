# Posição, direção e referência do pan — 30/09/2026

Implementação autorizada após a análise dos testes filmados no Samsung SM-S916B. A meta de bancada é erro máximo de 10 cm no eixo e 10° na direção, sem acrescentar marca ao scanner. Essa meta ainda exige medição física; testes de software e dispersão interna não medem o erro absoluto.

## Posição estável antes de capturar

Uma posição estacionária fica provisória até receber três estimativas distintas, em pelo menos dois pontos de vista separados por 10 cm, ao longo de pelo menos 0,4 s, com dispersão de no máximo 10 cm. Repetir o mesmo pacote não confirma a posição. Uma estimativa incompatível inicia outra confirmação, preservando a origem anterior.

A solução confirmada deve respeitar a altura observada do apoio mais a altura mecânica do eixo e o deslocamento da tag. Uma solução 3D livre pode servir ao diagnóstico, mas não confirma a posição estacionária nem libera o scan. O gate verifica novamente a altura atual, com tolerância vertical de 3 cm.

Mudanças maiores que 10 cm na origem ou maiores que 10° no yaw derivado invalidam a direção anterior. A captura fica bloqueada até nova confirmação. A origem fica congelada enquanto o pan gira, um comando está pendente ou há pontos capturados. Após perder o referencial AR, é preciso limpar a nuvem e relocalizar; os pontos antigos não podem ser misturados com uma origem nova.

O refino por profundidade agora usa observações novas mesmo sem uma nova solução UWB. Exige confirmação entre vistas e conserva a correção já aceita quando a evidência visual expira. Continua sendo uma aproximação da superfície do corpo; por si só não certifica a localização do eixo.

## Direção pela própria cabeça do scanner

O observador experimental procura a face laranja da tampa original do LiDAR na câmera do celular. Combina formato projetado, tamanho e posição mecânica conhecidos, altura do apoio, múltiplas vistas e proximidade da tag UWB. Não requer uma marca impressa.

O modelo nominal usa tampa circular de 68 mm de diâmetro, centro 88 mm à frente e 50 mm acima do eixo do pan. Essas dimensões devem coincidir com a montagem real. A altura do eixo sobre o apoio continua configurada em 100 mm; o deslocamento da tag usa a geometria informada pelo firmware quando disponível.

Exige pelo menos três observações em duas vistas, separação total de pelo menos 25 cm e lateral de pelo menos 20 cm, dispersão de centros de no máximo 4 cm e ausência de soluções concorrentes de yaw além de 8°. O lado frontal é identificado pela hipótese de que a face laranja visível é a tampa frontal original; um círculo isolado tem ambiguidade de 180°. A cor é apenas uma candidata: objetos marrons ou laranja de formato semelhante ainda podem confundir o método. Reflexos, oclusão e pouca resolução também podem impedir a confirmação.

A imagem deve corresponder ao quadro AR e ser processada enquanto o celular permanece parado. A conversão assíncrona é consultada na thread principal. Quadros antigos, capturados antes de uma mudança de referencial, pan ou apoio são rejeitados. Origem e direção visuais são aplicadas juntas. A pose visual confirmada não é puxada de volta por uma nova solução UWB enviesada.

Antes da direção conhecida, o indicador mostra somente a vertical. O alinhamento automático por um ponto arbitrário na mesa fica desativado por padrão; o alinhamento manual guiado continua disponível. A rotação local de montagem de 200° é preservada.

## Zero do motor e memória flash

O firmware v8 oferece confirmação de zero físico com a cabeça parada. O operador alinha a frente do LiDAR à frente física escolhida para a base e confirma no aplicativo. O comando não procura um fim de curso nem gira a cabeça automaticamente.

O ESP32 salva uma referência versionada com posição em passos, configuração mecânica, checksum e indicação de parada limpa. Antes de habilitar pulsos, grava que o movimento está em andamento. Depois de confirmar a parada, salva a posição final. Não grava a cada passo.

Na reinicialização, restaura somente uma referência íntegra, compatível com a configuração e salva após parada limpa. Se houver corte durante o giro, referência inválida ou falha de armazenamento, exige confirmação física novamente. A contagem de passos não detecta passos perdidos, movimento manual com a placa desligada ou movimento da base inteira.

O zero do motor define o ângulo da cabeça em relação à base. O yaw da base no mundo AR continua exigindo observação da câmera ou alinhamento manual, especialmente após mover a base. Voltar ao zero apenas retorna à referência confirmada.

## Comandos e diagnóstico

Iniciar scan passa a mostrar estado pendente e só entra em captura após confirmação recente do scanner. Uma resposta HTTP iniciada antes do comando, inclusive no mesmo quadro do aplicativo, não confirma a operação. Após cinco segundos sem confirmação, o pedido é encerrado com mensagem explícita.

O CSV mantém as 95 colunas anteriores e acrescenta 32: identificadores de pacote e ciclo processado, decisão/retry, candidatos, versão e referencial da pose, altura consistente, motivo de confirmação/congelamento, referência do pan, estado visual e comando pendente. Há linhas de decisão mesmo quando nenhum novo pacote chegou. Portanto, uma primeira consulta sem histórico AR pode ser distinguida de um retry bem-sucedido no quadro seguinte.

## Próximo ensaio de bancada

1. Gravar o firmware novo com o ESP32 conectado por USB. Confirmar o zero com a cabeça fisicamente alinhada e parada.
2. Manter o scanner parado sobre apoio horizontal. Conferir a altura mecânica de 100 mm e as dimensões da tampa usadas no modelo.
3. Observar apoio e corpo, pausar em pontos separados para confirmar UWB e manter a tampa laranja inteira visível de duas vistas com cerca de 25–50 cm de separação lateral. Aproximar o celular se a tampa aparecer muito pequena; evitar vista quase de perfil e fios cobrindo a face.
4. Esperar posição e direção confirmadas; conferir visualmente o eixo antes de iniciar o scan. Se mover a base, usar “Movi o scanner: refazer posição e direção”.
5. Medir o eixo com régua e a direção contra uma referência física. Repetir em distâncias e ângulos diferentes, registrando erro máximo. Só esse ensaio confirma ou refuta 10 cm / 10°.
6. Verificar persistência após parada e reinicialização; após interrupção durante movimento, verificar que o app solicita nova confirmação. Não redefinir zero com a cabeça fora da posição física escolhida.

Após reconectar o celular, o APK final foi instalado por USB no Samsung SM-S916B, preservando os dados existentes. A abertura do menu foi verificada e o perfil UWB salvo continua visível. Após conectar o scanner em COM6, o firmware foi gravado no ESP32-S3 e o conteúdo foi verificado. A inicialização pela serial confirmou o pan sem referência física, a tag DW1000 operacional e o AP/TCP em 192.168.4.1. A confirmação de zero e o ensaio de precisão ainda dependem do alinhamento físico pelo operador.

## Validações de software

O wrapper `ScannerPoseValidation.BuildValidated` passou as regressões de altura e salto, candidato/commit, preservação do refino, interpolação e mudança de referencial AR, comandos e respostas HTTP antigas, referência de pan, indicador sem yaw conhecido, projeção independente de disco preenchido, transformações de imagem e entradas inválidas. O CSV foi conferido com 127 colunas únicas. Log: `diagnostics/20260930/implementation-build.log`.

O firmware foi compilado para `esp32s3` com os testes nativos de persistência e matemática espacial aprovados. Arquivo preparado: `Builds/ScannerESP32S3-pan-reference-20260930.bin`. A validação visual é sintética; a detecção e precisão na montagem real continuam pendentes do ensaio acima.

O APK final foi compilado com sucesso em `Builds/ScannerAR-pose-pan-20260930.apk` (aproximadamente 36,4 MB), pacote Android e assinatura do fluxo de compilação existente, ARM64, Android mínimo API 26. A revisão final inclui os textos que orientam observar a tampa em vez de alinhar automaticamente pela mesa. Log final: `diagnostics/20260930/implementation-build-final.log`. Os tamanhos e hashes dos dois artefatos estão em `diagnostics/20260930/delivery.json`.

Verificação após instalação: processo ativo e menu exibido, aguardando scanner Wi-Fi e base UWB USB-C. Evidência em `diagnostics/20260930/installed-app-screen.png`. O log inicial registra uma exceção de classe opcional `AssetPackManager` do Unity; a inicialização prosseguiu e o menu foi exibido. Esta verificação não inclui captura física nem alinhamento em AR.

Após gravação: firmware idêntico ao binário validado (SHA-256 registrado em `delivery.json`), upload aprovado e boot conferido em `firmware-installed-boot.log`. A referência inicial foi `unreferenced`, com 0 passos, como esperado: isso não estabelece o zero físico. Nenhum comando de movimento ou confirmação de zero foi enviado.

Na mesma inicialização, a MLX90640 apareceu disponível, mas o log mostrou instabilidade de I2C, redução automática de 8 Hz/400 kHz para 4 Hz/100 kHz e reinicializações repetidas. Essa observação fica registrada para revisão separada; não certifica estabilidade térmica nem explica por si só o alinhamento do eixo.
