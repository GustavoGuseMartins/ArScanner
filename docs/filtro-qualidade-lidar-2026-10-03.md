# Qualidade dos retornos LiDAR — 03/10/2026

O autor pediu preservar o alcance disponível e evitar pontos ruins, sem introduzir filtro de distância. A revisão acrescenta uma opção de rejeitar retornos com o aviso de sinal fraco do próprio sensor. Ela começa **desligada**: o aviso não prova que a distância esteja errada. O alinhamento manual, a calibração UWB e a opacidade permanecem no fluxo existente.

## Informação recebida

No protocolo de 22 bytes usado pelo scanner, cada medida contém uma marca de distância inválida, um aviso de intensidade e o valor de intensidade de 16 bits. Esses campos são distintos. [Formato publicado pela Roborock](https://github.com/Roborock-OpenSource/Cullinan#lds-serial-data).

O parser já descartava medidas marcadas como inválidas e pacotes com checksum incorreto. Agora também preserva `signalStrength` e `strengthWarning`. O aviso é transportado no bit `0x08` dos flags existentes; o pacote TCP continua com 28 bytes, sem alterar coordenadas ou informações térmicas. A intensidade bruta e o aviso aparecem no CSV do scanner como `signal_strength` e `strength_warning`. Não foi escolhido um limiar arbitrário de intensidade.

## Uso no aplicativo

Com a captura pausada e o scanner atualizado, a opção **Ignorar retornos com sinal fraco** permite comparar os dois resultados. A troca limpa a nuvem e a fila recebida, preservando a pose do eixo. Quando ligada, o descarte ocorre antes da atualização dos voxels e das temperaturas. Os diagnósticos mostram contadores de amostras válidas, retornos com aviso, medidas inválidas e erros de checksum; são contadores do scanner desde a inicialização, não quantidades únicas de pontos na nuvem.

Nenhum limite de distância novo foi acrescentado. Um retorno distante válido, sem aviso, continua entrando. Superfícies com retorno fraco podem desaparecer ao ativar a opção mesmo que sua distância seja válida. Por isso, a comparação física deve observar simultaneamente a redução de pontos isolados e a preservação de paredes/objetos reais.

O filtro não identifica todos os erros possíveis: reflexos, geometria ou associação temporal podem produzir um ponto errado sem o aviso. A intensidade bruta ficou disponível para uma análise posterior que justifique qualquer filtro mais específico.

## Validação

Os testes nativos verificam o parser, independência das flags, decodificação completa da intensidade e preservação dos dados no CSV. A validação Unity exercita o pacote TCP, o descarte real antes da fusão dos voxels, a validade térmica independente, a troca da opção e a recepção de um ponto forte a 16 m. Esse último caso testa a regra de software; não certifica alcance físico de 16 m.

Esses testes passaram, e o APK foi compilado com saída Unity 0 e assinatura verificada: [ScannerAR-lidar-quality-20261003.apk](../Builds/ScannerAR-lidar-quality-20261003.apk). O firmware v12 foi gravado na COM6 com integridade confirmada; a observação USB posterior mostrou a MPU pronta, sem erros de leitura, e os quadros térmicos avançando. O APK ainda não foi instalado porque não havia celular conectado por ADB. Tamanhos, hashes e registros de validação estão em [lidar-quality-delivery.json](../diagnostics/20261003/lidar-quality-delivery.json).

O ensaio de qualidade com o LiDAR em rotação ainda depende da alimentação principal. A revisão não foi usada para certificar a montagem I²C da GY-25: no módulo correspondente ao [manual GY-25](https://www.gysensor.cn/wp-content/uploads/GY-25%E5%80%BE%E6%96%9C%E8%A7%92%E5%BA%A6%E4%BC%A0%E6%84%9F%E5%99%A8%E6%A8%A1%E5%9D%97%E4%BD%BF%E7%94%A8%E6%89%8B%E5%86%8C2.pdf), a ponte I²C identificada deve ser fechada com todas as alimentações desligadas. A leitura breve da MPU não dispensa essa seleção de interface.
