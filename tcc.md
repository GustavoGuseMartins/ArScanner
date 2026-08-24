# 📄 Especificação Técnica: Scanner 3D Aéreo com Fusão de Dados Térmicos e Realidade Aumentada (AR)

**Autores:** Gustavo Guse Martins & Kevin Kurpias Rodrigues  
**Orientador:** Jonathan de Matos  
**Instituição:** Universidade Estadual de Ponta Grossa (UEPG) - Engenharia de Computação  
**Status do Projeto:** Fase de Modelagem 3D, Roteamento de PCB e Escrita de Firmware  

---

## 1. Visão Geral do Projeto
O projeto consiste na criação de um scanner 3D aéreo acoplado a um drone. O sistema coleta nuvens de pontos no espaço através de um LiDAR, cruza essa malha espacial com dados de temperatura (Câmera Térmica IR) e cor (Câmera RGB), e transmite esses dados para um visualizador em Realidade Aumentada (desenvolvido em Unity). 

O diferencial arquitetônico é o sistema de **Trilateração Espacial via UWB (Ultra-Wideband)**: o scanner aéreo atua como *Tag* móvel, enquanto o visor AR possui 3 módulos UWB atuando como *Âncoras*, permitindo que o Unity ancore a nuvem de pontos tridimensional com precisão milimétrica no mundo real do usuário, eliminando o "drift" da Realidade Aumentada clássica.

---

## 2. Arquitetura Mecânica (O Payload Suspenso)
Para manter o peso de decolagem abaixo do limite de 500g (estimativa atual: ~492g) e garantir estabilidade na coleta de dados, a mecânica foi desenhada para operar sob **força de tração gravitacional**.

### 2.1. Eixo e Rotação
* **Rolamento Principal:** Utiliza-se um Rolamento Rígido de Esferas (ex: série 6800 ou 608ZZ) fixado em um "copo" (housing) com amortecedores de vibração (dampers) na barriga do drone. Este rolamento suporta 100% da carga axial (peso da estrutura).
* **Slip Ring:** Modelo Intelbras VIP 5225 SD (20 vias, 28 AWG). O eixo de suspensão cruza pelo slip ring para levar a energia da bateria estática no drone para a placa giratória. As vias são paralelizadas (5 para VCC, 5 para GND) para suportar a corrente do sistema (~1.5A pico) sem queda de tensão.
* **Motorização:** Motor NEMA 14 Pancake (86g) posicionado na estrutura giratória (*Head Unit*). O eixo do motor conecta-se ao pedestal fixo via acoplador flexível de alumínio. O motor apenas gera o torque de torção em passos precisos para o escaneamento, sem carregar o peso do conjunto.

---

## 3. Arquitetura Elétrica e Hardware

### 3.1. Distribuição de Energia (Power Tree)
O sistema é alimentado por uma bateria **LiPo 3S 20C (11.1V, 86g)** localizada na base fixa. A tensão cruza o slip ring e é dividida em dois barramentos na placa customizada (PCB) através de reguladores Step-Down isolados.

* **Barramento 12V (VCC RAW):** Alimenta diretamente o driver do motor de passo (TMC2209).
* **Barramento 5V:** Regulado por um módulo **MP1584EN** (Step-down robusto para dissipação térmica). Alimenta o microcontrolador ESP32-S3 e o motor rotativo do próprio LiDAR.
* **Barramento 3.3V:** Regulado por um módulo **Mini360** (ultracompacto). Alimenta os módulos I2C, SPI e a lógica do driver.

### 3.2. Microcontrolador e Mapeamento de Pinos (Pinout)
O "cérebro" da *Head Unit* é um **ESP32-S3 N16R8** (clone modelo Freenove WROOM CAM). Devido ao uso intenso de pinos pela memória PSRAM Octal e interface DVP da câmera nativa, os GPIOs foram meticulosamente roteados para as bordas seguras:

| Periférico / Barramento | Pinos (ESP32-S3) | Função |
| :--- | :--- | :--- |
| **Câmera Térmica (MLX90640)** | GPIO 47, 48 | I2C (SDA, SCL) - Compartilhado |
| **IMU / Giroscópio (MPU6050)** | GPIO 47, 48 | I2C (SDA, SCL) - Compartilhado |
| **UWB (BU01 / DW1000)** | GPIO 39, 40, 41, 42, 2 | SPI (MOSI, MISO, SCK, CS, IRQ) |
| **LiDAR (Roborock)** | GPIO 1, 3, 21 | UART (TX, RX) + PWM do motor |
| **Driver de Passo (TMC2209)** | GPIO 45, 46 | STEP, DIR (EN aterrado) |
| **Câmera RGB (OV2640)** | Pinos Nativos FPC | Interface DVP + Barramento SCCB |

---

## 4. Arquitetura Lógica e Dados

### 4.1. Unidade Móvel (Scanner/ESP32-S3)
* Processamento FreeRTOS em múltiplos núcleos.
* **Core 0:** Dedicado à leitura ininterrupta do pacote de dados da UART do LiDAR e buffer da câmera OV2640.
* **Core 1:** Fusão de sensores. Associa o ângulo atual do NEMA 14 com a matriz 32x24 térmica (MLX90640) e com a correção de inclinação do giroscópio (MPU6050).
* Empacota o dado `(Ângulo_Base, Ângulo_LiDAR, Distância, Temp, RGB)` e transmite via Wi-Fi/UDP para o renderizador.

### 4.2. Unidade Receptora (Visor AR)
* Uma segunda PCB contendo outro ESP32.
* Possui **3 Módulos UWB BU01** conectados em barramentos SPI distintos (ou usando Chip Selects diferentes).
* Responsável por enviar sinais de rádio para o Scanner, calcular o Time-of-Flight (ToF) de retorno e realizar o cálculo matemático de trilateração 3D ($X, Y, Z$).

### 4.3. Unity (Renderização)
* Recebe a malha de pontos do scanner e a posição espacial relativa vinda da unidade receptora.
* Instancia um `Mesh` ou `Particle System` onde as coordenadas dos pontos são colorizadas com base no espectro da câmera térmica (ex: azul para frio, vermelho para quente) ou da textura RGB, gerando um mapa espacial imersivo termo-visual através de paredes/ambientes.

---

## 5. O Que Precisa Ser Feito (Tarefas e Pendências)

Aqui está o escopo de trabalho atual para o qual preciso de sua ajuda e processamento ("antigravity"):

1. **Modelagem 3D (CAD):** 
   * Precisamos projetar os arquivos `.stl`/`.step` das carcaças impressas em 3D.
   * Modelar o engate do rolamento e amortecedores (dampers) no drone.
   * Modelar a *Head Unit* balanceando o centro de gravidade (colocando a PCB, o NEMA 14 Pancake e os sensores para que o conjunto pendurado fique nivelado, não forçando o motor de passo como um pêndulo).
2. **Design da PCB (KiCad):**
   * Finalizar o roteamento físico (Pcbnew) baseado na *Netlist* definida acima.
   * Usar *footprints* modulares (barras de pinos) para o ESP32-S3 e os Step-Downs, criando as *Keep-Out Zones* no plano de terra ao redor da antena do UWB.
3. **Firmware C++ (ESP-IDF / PlatformIO):**
   * Escrever a lógica de *timing* rigoroso que associa o pacote recebido pelo LiDAR ao exato ângulo/passo que o motor TMC2209 está executando naquele milissegundo.
   * Implementar o algoritmo TWR (*Two-Way Ranging*) para o ESP32 do visor interrogar os 3 UWBs e calcular a trilateração sem *bottleneck* de processamento.
4. **Scripting Unity (C#):**
   * Desenvolver a camada de rede (UDP Socket) para ingestão da nuvem de pontos em tempo real.
   * Criar o *shader* ou material capaz de mapear a matriz de calor em cima da malha de geometria instanciada, compensando a diferença de campo de visão (FOV) do LiDAR vs. Câmera Térmica.