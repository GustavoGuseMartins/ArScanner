# GY-25 e térmica após a solda — 03/10/2026

O usuário informou ter fechado as duas pontes da GY-25 e reconectou o scanner por USB. A observação foi passiva: nenhum comando serial ou de motor foi enviado. Foram coletados 55 segundos de serial, com onze snapshots de cada sensor abrangendo 50,07 segundos. A posição das soldas na placa física não foi inspecionada; a imagem enviada foi um recorte de documentação, não uma foto da montagem.

## Significado das pontes

O [manual original GY-25 V1.0 do fabricante](https://www.gysensor.cn/wp-content/uploads/GY-25%E5%80%BE%E6%96%9C%E8%A7%92%E5%BA%A6%E4%BC%A0%E6%84%9F%E5%99%A8%E6%A8%A1%E5%9D%97%E4%BD%BF%E7%94%A8%E6%89%8B%E5%86%8C2.pdf), páginas 2 e 3/Figura 1, foi baixado, extraído e conferido visualmente. A cópia de referência está em `diagnostics/20261003/gy25-manufacturer-manual.pdf`.

- Ponte esquerda na Figura 1: aberta seleciona UART 115200; fechada seleciona UART 9600. Não define a frequência do I²C nem a comunicação USB do ESP32.
- Ponte direita: fechada seleciona I²C, bloqueia a serial do módulo e o torna equivalente a uma MPU6050 direta, com seleção de endereço baixa (0x68).

O recorte inglês enviado pelo usuário contém “Open: I2C to MPU6050 / Close: I2C to Output”. Essa legenda isolada é ambígua; não especifica uma saída de ângulos Euler processados por I²C. Foi precipitado tratá-la, na atualização intermediária do chat, como demonstração de dois modos de aquisição contraditórios ao manual. O texto e a Figura 1 do original esclarecem a seleção UART/I²C. O termo “Output” não substitui uma especificação de protocolo.

O anúncio de compra informado foi `https://pt.aliexpress.com/item/1005008095518071.html`. A leitura web não retornou conteúdo, e a abertura no navegador foi bloqueada pela política de segurança do navegador. Nenhuma imagem/variante do anúncio foi confirmada e nenhum caminho alternativo de acesso ao anúncio foi tentado após esse bloqueio. A conclusão operacional abaixo vem do firmware e das leituras reais, junto ao manual original, não da identificação visual do produto vendido.

## Resultado observado

| Medida | Resultado |
| --- | --- |
| Identidade MPU | 104 (WHO_AM_I 0x68) em todos os snapshots |
| MPU pronta e bias | Ambos verdadeiros em todos os snapshots |
| Erros de leitura MPU | 0 → 0 |
| Tentativas de inicialização MPU | 3 → 3; não houve nova tentativa no intervalo |
| Idade da última amostra MPU | 0–52 ms |
| Norma da aceleração | 0,967–0,975 g |
| Lacunas do integrador MPU | 0 → 0 |
| Quadros térmicos | 340 → 535 em aproximadamente 50 s |
| Quadro térmico disponível | Sim, em todos os snapshots |
| Tempo da leitura térmica | 51–53 ms |
| Idade do quadro térmico | 126–322 ms |
| Erros I²C da térmica | 0 → 0 |
| Timeouts / overruns térmicos | 1 → 1 / 1 → 1; nenhum novo evento observado |
| Pixels térmicos mascarados | 18, como no registro anterior |

Isso confirma aquisição da MPU e quadros térmicos no mesmo barramento durante o ensaio. O firmware está lendo registradores reais de aceleração/gyro; não interpreta pacotes Euler da GY-25. A térmica permanece parcial devido à máscara já existente. A observação não certifica a precisão da fusão térmica em movimento.

A cadência observada melhorou frente ao registro anterior de aproximadamente 174 ms por leitura térmica e quatro lacunas IMU por segundo. Agora foram 51–53 ms e nenhuma lacuna no intervalo. O ensaio não isola a causa dessa diferença: não foi uma comparação controlada de velocidade do barramento, estado térmico e solda. A limitação do código que exclui intervalos acima de 100 ms continua existindo, mesmo sem novos intervalos desse tipo no ensaio atual.

O ângulo Z do sensor variou de 1,71° a 2,68°. Houve uma amostra de gyro com norma de 1,79°/s, e não foi estabelecido repouso físico estrito para uma medição de deriva. Essa diferença não é uma estimativa controlada do bias nem uma calibração de yaw absoluto.

Evidências: [serial original](../diagnostics/20261003/usb-post-solder-observation.log) e [resumo numérico](../diagnostics/20261003/usb-post-solder-summary.json).

## Próxima verificação necessária

Não há necessidade demonstrada de desoldar a ponte de velocidade serial ou trocar o driver. A compensação IMU permanece desligada. Antes de usar a orientação na nuvem, conferir a montagem sensor→cabeça com poses físicas conhecidas, mantendo cada pose por alguns segundos e sem forçar os eixos mecânicos. Depois avaliar estabilidade angular com repouso controlado e repetir a coexistência com a alimentação principal na condição normal de captura. A posição X/Z e a referência inicial de direção no mundo AR ainda exigem a observação espacial existente; o acesso à MPU não fornece essas referências sozinho.
