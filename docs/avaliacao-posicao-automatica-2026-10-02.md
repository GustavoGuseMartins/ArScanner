# Validação automática de X/Z e revisão elétrica/mecânica — 02/10/2026

Atualização de 03/10: a MPU6050 voltou a fornecer leituras no firmware v11. O adendo ao final atualiza o estado físico e a limitação temporal, preservando abaixo a análise original de 02/10.

## Conclusão

**Esclarecimento após a pergunta sobre a MPU6050:** a proposta abaixo de conferir X/Z usando yaw manual não automatiza a calibração completa da rotação. O autor destacou essa limitação. A MPU6050 mede rotação relativa e inclinação, mas não fornece a direção horizontal inicial no referencial AR: contém acelerômetro e giroscópio, sem magnetômetro interno. No firmware, `ImuSensor::begin` zera `angleZ`; `update` integra `gyroZ`, e `getYaw` não é usado pelo fluxo de alinhamento. Além disso, os registros recentes mantêm `imuReady=false`. Aproveitá-la exige recuperar leituras, mapear os eixos da montagem e medir deriva; não basta habilitar o yaw para remover a referência inicial. [Datasheet oficial MPU6050](https://product.tdk.com/system/files/dam/doc/product/sensor/mortion-inertial/imu/data_sheet/mpu-6000-datasheet1.pdf).

É possível acrescentar uma verificação visual independente da posição horizontal do eixo, preservando a rotação manual que funcionou bem. O caminho de menor impacto é primeiro usar a geometria original do scanner como **comparação de posição**, sem substituir automaticamente a pose aceita. Já existe no projeto um observador experimental da tampa laranja do LiDAR, desligado por padrão. Sua precisão física ainda não foi comprovada e não há evidência nesta revisão para ligá-lo por padrão ou relaxar os limites do UWB.

O autor confirmou nesta sessão que prefere **somente as peças atuais**, sem alvo visual impresso. Assim, a recomendação ativa é explorar a tampa original como validador de X/Z, aproveitando a direção manual. A comparação com alvos conhecidos abaixo documenta o limite das alternativas; ela não é uma proposta de alteração da montagem para esta revisão.

Nenhum parâmetro de montagem, firmware ou código Unity foi alterado nesta revisão. A análise das gravações/logs da sessão recente é uma frente separada; as conclusões abaixo tratam da observabilidade e da montagem, não medem o erro real dessa sessão.

## Arquivos examinados e rastreabilidade

- `tcc.net` e `tccvis.net`, lidos por `tools/inspect_netlists.py`.
- `scanner_v2_montado.blend` e `scanner_v2_impressao.blend`, pelos inventários existentes `docs/scanner_montado_inventory.json` (229 objetos) e `docs/scanner_impressao_inventory.json` (67 objetos), produzidos por `tools/inspect_scanner_blend.py`. Foi inspecionado também o render `docs/scanner_montado.png`.
- Os inventários identificam os arquivos originais em `Downloads`. As cópias do projeto e de `Downloads` têm SHA-256 idêntico. Nesta máquina não foi encontrado executável Blender, portanto não foi feita nova extração da cena.
- SHA-256 montado: `39B921F11339EB7248F92D694691DD62F47A9A1B06BEEBDF8BCDEC05C3613A64`.
- SHA-256 impressão: `D654BF22F748ED32DA8A79B2B60466E11F6B33E5148AF3547CC363706484CE96`.
- Documentação de montagem e ensaios: `docs/calibracao-montagem-real.md`, `docs/uwb-calibracao-2026-09-26.md`, `docs/plano-fusao-ar-uwb.md`, `docs/pose-fusao-2026-09-27.md`, `docs/ensaio-auto-posicionamento-2026-09-28.md`, `docs/analise-deslocamento-2026-09-28.md` e `docs/implementacao-pose-pan-2026-09-30.md`.
- Implementação atual: `ScannerDiskPoseEstimator.cs`, `ScannerVisualPoseObserver.cs`, `UwbArMultiviewEstimator.cs`, `UwbAnchorManager.cs`, `firmware/scanner/include/config.h`, `firmware/viewer/include/config.h` e `Packages/manifest.json`.

## O que a elétrica permite concluir

| Evidência nas netlists | Consequência para localizar o eixo |
| --- | --- |
| Scanner: uma tag DWM1 ligada ao ESP32-S3 por SPI; visualizador: três DWM1000 DWM2/3/4 em SPI compartilhado, com seleções e IRQ separados. | Há três alcances até uma única tag. A posição da tag não determina sozinha a direção da base. |
| Reset de DWM1 sem ligação ao ESP32; reset das três âncoras ligado em paralelo a IO4. | Confirma a topologia já refletida no firmware; não fornece observação de posição ou zero mecânico. |
| TMC2209 com STEP/DIR, EN aterrado; INDEX, DIAG e UART não ligados ao ESP32. | A netlist não oferece encoder ou sensor de homing para conferir o pan. A persistência da contagem de passos continua dependente da referência física confirmada pelo operador. |
| GPIO4/5 ligados a MOSI/MISO da tag. | A câmera OV2640 na pinagem candidata S3-CAM conflita com o UWB, conforme o diagnóstico atual. Para validar o eixo pela visão, usar a câmera do celular evita mexer nessa placa. |
| Alimentação e capacitores aparecem nas redes, mas a netlist não contém desenho de cobre ou posição física de antenas. | Não é possível concluir folga RF, estabilidade sob carga ou centro de fase pela netlist. A ausência de um novo erro de enlace não justifica alteração elétrica. |

A geometria 100/100/175 mm das três antenas do visualizador vem das medidas registradas pelo autor em 26/09 e dos parâmetros ativos, não de `tccvis.net`. Ela gera altura de 48,412 mm para o triângulo. A fusão com diferentes posições do celular permanece necessária porque as antenas estão muito próximas entre si em relação à distância do scanner. A Qorvo mantém documentação específica sobre atraso de antena e efeitos de canal para o DWM1000; a calibração de alcance e a diversidade de vistas resolvem problemas diferentes. Não há indicação aqui para trocar canal, potência, atraso ou filtro sem medição. [Documentação oficial DWM1000](https://www.qorvo.com/products/p/DWM1000).

## Medidas extraídas da mecânica

O Blender usa Z vertical. Na posição montada nominal, o LiDAR está na direção -Y do Blender, correspondente à direção frontal +Z no aplicativo. Não copiar coordenadas entre esses referenciais sem a transformação.

| Peça ou conjunto | Medida nominal extraída | Uso/limite |
| --- | --- | --- |
| Base fixa com berço de bateria | 138 × 88 × 50 mm | Referência rígida mais apropriada para um alvo visual que não gira junto com a cabeça. O envelope não garante uma face livre de 138 × 88 mm. |
| Ponte fixa do slip ring/eixo | 58 × 59,7 × 18 mm; altura 50–68 mm | É fixa, porém pequena e parcialmente encoberta na montagem. |
| Engrenagem fixa | 180 dentes; módulo 0,50 nominal; envelope Ø91 × 7 mm | Está fixa à base. O pinhão está identificado como 10T; a redução nominal 18:1 coincide com o firmware. |
| Mesa da estrutura móvel | 88 × 138 × 4 mm; altura 96–100 mm | É parte móvel, parentada a `PAN`. Não confundir com a base fixa ao escolher a posição de uma marca. |
| Quadro vertical | 88 × 34 × 141 mm; altura 100–241 mm | Também gira com o pan. |
| Tampa frontal original do LiDAR | Ø68 × 2 mm; centro Blender `(0; -88; 147)` mm | Coincide com raio de 34 mm e avanço de 88 mm usados no modelo visual atual. |
| Fenda óptica LiDAR | Centro Blender `(0; -79; 147)` mm | Explica os 79 mm nominais do avanço óptico no firmware atual. Tampa e centro óptico são referências distintas. |
| PCB principal | 60,5 × 1,6 × 123 mm; centro `(0; -29,2; 173,5)` mm | Modelo nominal; sensores/placas incluem elementos explicitamente ilustrativos. |

As peças de impressão confirmam os envelopes da base, ponte, mesa e quadro. Há cópias de apresentação e peças de impressão no mesmo inventário montado; os extremos globais de todos os objetos não representam o tamanho do scanner montado.

A altura do eixo sobre o apoio está configurada em **100 mm**. O modelo visual considera a tampa **50 mm acima do eixo**. Isso coloca seu centro nominal em 150 mm sobre o apoio; no Blender ele está em 147 mm. A diferença de 3 mm não justifica mudar o parâmetro medido: é necessário confirmar no exemplar real qual plano é a origem adotada. Os docs antigos também citam 90 mm de avanço óptico, enquanto o firmware atual usa os 79 mm extraídos da fenda. Não foi reaberta a calibração que já produziu a nuvem boa; registrar quais valores estavam ativos no ensaio é o passo correto.

**Limite de identificação da tag:** o comentário de `TAG_PHYSICAL_OFFSET_X_M = -0.0135` atribui o valor a uma blindagem DWM1000 no Blender. O objeto naquela posição se chama `blindagem_1 | Blindagem do módulo ESP, ilustrativa`; o modelo contém também `esp_wroom | ESP32-WROOM: posição ilustrativa`. Não foi encontrado objeto identificando inequivocamente a antena DWM1000. Portanto, o inventário sozinho não confirma esse deslocamento lateral nem o centro de fase RF. O offset ativo `(-13,5; 120; 20)` mm deve continuar sendo tratado como parâmetro a conferir fisicamente, sem substituí-lo pela blindagem ilustrativa. O último bom alinhamento pode limitar o erro da montagem naquela orientação, mas não identifica a antena por si só.

## Alternativas para verificar automaticamente X/Z

| Alternativa | Informação obtida | Avaliação para este projeto |
| --- | --- | --- |
| Mais alcances UWB na mesma posição do celular | Redução de ruído aleatório | Não cria separação geométrica nem remove viés fixo. Aguardar mais parado no mesmo ponto pode não ajudar. |
| UWB com AR e vistas separadas | Posição da tag no mundo AR | Já implementado. É o método principal atual; manter perfil bom, pausas e deslocamentos que realmente contribuam para o ajuste. |
| Plano AR/Depth do apoio/corpo | Altura e superfície visível | Útil como restrição; não reconhece qual objeto é o scanner nem identifica o centro do eixo. Não certificar X/Z só com um ponto sobre o corpo. |
| Tampa original Ø68 mm + rotação manual conhecida | Posição horizontal inferida do centro da tampa, descontando os 88 mm da geometria | Menor mudança física. Usar primeiro como validador. Precisa de dimensão real, altura, visão inteira e identificação coerente entre vistas. |
| Alvo visual rígido de tamanho/posição conhecidos na base fixa | Pose da base e posição do eixo por transformação rígida | Caminho mais direto se uma marca for aceitável. Pode validar apenas X/Z e manter o yaw manual. Precisa medir o deslocamento alvo→eixo uma vez. |
| ICP/registro LiDAR contra Depth do celular | Transformação entre superfícies do ambiente | Complexidade maior. Uma parede ou mesa isolada deixa translação/rotação pouco observáveis; requer geometria variada e teste de convergência. Não é a primeira intervenção. |
| Encoder/homing/magnetômetro | Referência de rotação ou orientação | Não fornece X/Z no mundo AR. Não resolve diretamente esta dúvida de posição. |

O ARCore Depth estima profundidade com movimento e pode combinar sensor de profundidade quando disponível. Não exige ToF, e não é uma identificação semântica do scanner. A profundidade da imagem também é medida ao longo do eixo da câmera, não como comprimento do raio oblíquo. [Guia oficial Depth em AR Foundation](https://developers.google.com/ar/develop/unity-arf/depth/developer-guide).

### Limite específico do observador da tampa já existente

O reconhecimento natural está desligado por padrão. Exige pan parado/referenciado, altura do apoio, três observações, pelo menos duas vistas independentes, 25 cm de separação total e 20 cm horizontal, dispersão de centro até 4 cm e ambiguidade angular até 8°. Ele estima posição **e** yaw em conjunto. Torná-lo obrigatório acrescentaria uma etapa a um fluxo em que o yaw manual já funcionou.

A posição visual atual vem da interseção do raio da câmera com a altura conhecida do centro da tampa; ela não usa a distância UWB como medida visual de profundidade. Porém, depende da altura do apoio, da montagem de 50 mm e do referencial AR. Perto de uma vista horizontal essa construção amplifica erro de altura. Com o limite atual `|ray.y| = 0,12`, o fator horizontal é aproximadamente `sqrt(1 - 0,12²) / 0,12 = 8,27`: 5 mm de erro de altura podem gerar cerca de 41 mm de erro horizontal, mesmo que as observações concordem entre si. É uma análise geométrica local, não uma medição no aparelho.

Para uma futura validação somente de X/Z, a rotação manual aceita pode servir como entrada para calcular `eixo = centro_tampa - R(yaw_base + pan) × (0; 0,050; 0,088)` m. Isso evita depender de estimar outro yaw pela elipse, mas ainda exige confirmar a identidade da tampa e a posição entre vistas. Começar exibindo a diferença horizontal visual/UWB; não mover o eixo nem invalidar a direção por uma única detecção. Esta variante não foi implementada nesta revisão.

### Alvo visual opcional

O projeto já usa AR Foundation/ARCore 6.5.0. A API de rastreamento de imagens pode ser usada sem instalar ARCore Extensions. Uma imagem natural com detalhes não repetitivos e tamanho físico conhecido pode reduzir a etapa de estimar escala, mas deve estar em rastreamento completo antes de usar sua pose. Um simples QR/ArUco não é a imagem recomendada para o reconhecedor genérico de Augmented Images. [Guia oficial em AR Foundation](https://developers.google.com/ar/develop/unity-arf/augmented-images/guide), [boas práticas de Augmented Images](https://developers.google.com/ar/develop/augmented-images).

Se a escolha for um marcador ArUco/AprilTag pequeno, usar um detector próprio de fiduciais. A implementação ArUco do OpenCV obtém os quatro cantos e calcula pose com dimensão física do marcador e intrínsecos da câmera. É outra integração, não basta adicionar a imagem binária ao reconhecedor ARCore. [Tutorial oficial OpenCV](https://docs.opencv.org/4.x/d5/dae/tutorial_aruco_detection.html).

Em ambos os casos, colocar o alvo numa peça **fixa**, visível, sem encobrir sensores ou antenas. O desenho oferece uma base larga, mas o render não prova superfície livre adequada; a posição final precisa de foto/medida física. Não colocar o alvo no quadro/mesa móvel sem aplicar explicitamente o pan. Medir largura impressa e alvo→eixo; observar de duas vistas; rejeitar pose antiga/ambígua; congelar a origem validada durante a captura. A melhora de tempo é plausível por observabilidade direta, mas seu valor em segundos só pode ser medido no celular.

## Recomendação mínima e ensaio de decisão

1. Preservar yaw manual, sinais, geometria LiDAR e perfil UWB que deram o bom resultado. Resolver mensagens/esperas desnecessárias identificadas nos logs sem aceitar pose de qualidade inferior.
2. Antes de qualquer alteração geométrica, medir a altura do eixo sobre o apoio e o centro da antena da tag em relação ao eixo. Confirmar qual parte dos offsets veio de régua e qual veio de modelo ilustrativo.
3. Comparar UWB aceito com uma referência independente de X/Z. Inicialmente, teste a tampa original com o reconhecimento experimental e registre falhas; não torná-lo obrigatório. Caso se desenvolva a variante de posição com yaw manual, começar só com indicação de concordância/divergência.
4. Se a peça original não for reconhecida de modo repetível, conservar UWB e ajuste manual e registrar os casos de falha. A preferência atual do autor exclui acrescentar um alvo impresso à montagem.
5. Em pelo menos cinco sessões reiniciadas, medir tempo até apoio reconhecido, pose UWB confirmada, rotação manual concluída e primeiro ponto aceito; medir erro X/Z em relação ao eixo físico. Repetir com posições do celular e pan diferentes, e com um objeto laranja próximo para verificar falso reconhecimento.

Uma pose internamente estável não é uma posição fisicamente correta. Só promover o validador a correção automática depois de registrar erro real, ausência de falso alvo e tempo menor, com uma saída manual disponível. As metas anteriores de 10 cm e 10° continuam metas de ensaio, não precisão demonstrada por esta nota.

## Adendo de 03/10 — MPU funcional e cadência para fusão

Após a gravação do firmware v11 pelo responsável principal, com os motores parados e o scanner conectado por USB, a MPU confirmou identidade `0x68`, bias calibrado, uma tentativa de inicialização e zero erros de leitura. A coleta final registrou aceleração próxima de `(-0,934; -0,016; 0,290)` g, norma aproximada de 0,979 g e giroscópio próximo de zero nas amostras impressas. O seletor do GY-25 continuou aberto em SDA/SCL: o acesso direto foi observado nessa condição, mas o teste breve não certifica o modo físico nem o isolamento do processador interno.

**Correção de 03/10 sobre a ponte I²C:** a interpretação anterior de que a leitura dispensava alterar a ponte foi excessiva. O [manual do fabricante GY-25](https://www.gysensor.cn/wp-content/uploads/GY-25%E5%80%BE%E6%96%9C%E8%A7%92%E5%BA%A6%E4%BC%A0%E6%84%9F%E5%99%A8%E6%A8%A1%E5%9D%97%E4%BD%BF%E7%94%A8%E6%89%8B%E5%86%8C2.pdf) exige fechá-la para bloquear a serial e permitir acesso direto à MPU6050. Identificar a marcação do módulo e a ponte indicada; se corresponderem ao manual, fechá-la com todas as alimentações desligadas e repetir o teste com a câmera térmica. Não foi demonstrada outra variante, conflito interno ou relação entre esse seletor e as lacunas abaixo.

Ainda existe uma limitação para acompanhar direção automaticamente: as lacunas de integração passaram de 260 para 340 em 20 segundos, cerca de quatro por segundo. Os últimos intervalos de 6,9–7,1 ms não significam aquisição continuamente nessa taxa; a idade observada foi 173–174 ms. A leitura térmica de 174 ms ocorre na mesma tarefa, e o driver recusa integração acima de 100 ms. A térmica continuou adquirindo quadros parciais, com 18 pixels mascarados. [Coleta física final v11](C:/Users/aaata/Projetos/tcc/ArScanner/diagnostics/20261003/usb-v11-final.log).

Assim, a falta de leitura da MPU registrada em 02/10 foi superada no ensaio atual, mas orientação da montagem, amostragem contínua e deriva ainda precisam de validação. FIFO e data-ready são candidatos para preservar amostras durante a aquisição térmica; DMP não cria referência horizontal absoluta. Esses recursos não foram ativados. A MPU pode ajudar a conferir movimento e direção relativa, enquanto X/Z continuam dependendo de UWB/AR e de uma observação independente com as peças existentes. [Auditoria atual do hardware](C:/Users/aaata/Projetos/tcc/ArScanner/docs/aproveitamento-hardware-2026-10-03.md).

O APK `ScannerAR-gy25-diagnostics-20261003.apk` foi gerado com testes Unity aprovados, mas não instalado porque o celular não estava conectado por ADB. Esta coleta não demonstra alinhamento automático ou nuvem corrigida pela MPU; a compensação de inclinação permaneceu desligada.
