# ACC Telemetry Studio

O **ACC Telemetry Studio** e uma aplicacao desktop em C# / .NET desenvolvida com Avalonia UI para analise de dados de telemetria gerados pelo simulador **Assetto Corsa Competizione (ACC)** atraves do formato de arquivos MoTeC (.ld e .ldx).

O projeto elimina a necessidade de ferramentas externas pesadas, oferecendo uma interface moderna com graficos sincronizados, mapa da pista em tempo real, calculo de diferenca de tempo (Time Delta) e persistencia em banco de dados embutido de alta velocidade.

---

## Arquitetura da Solucao

A solucao foi desenhada seguindo principios de separacao de responsabilidades e Domain-Driven Design (DDD), dividida nos seguintes projetos:

* **AccTelemetry.Domain**: Modelos centrais de negocio (`Session`, `Lap`, `LapTelemetry`), catalogo de extensoes de pistas oficiais e gerador matematico de mapa 2D.
* **AccTelemetry.IO.Motec**: Motor de parsing binario para decodificacao do formato MoTeC (.ld) via `Span<byte>` / `BinaryPrimitives`, extrator XML de marcadores de volta (.ldx) e normalizador de telemetria.
* **AccTelemetry.IO.Persistence**: Camada de acesso a dados com SQLite (`Microsoft.Data.Sqlite`). Armazena arrays numericos colunares (`double[]`) diretamente como BLOBs em memoria bruta com transacoes atomicas em lote e modo WAL.
* **AccTelemetry.UI**: Interface grafica multiplataforma desenvolvida em Avalonia UI e ScottPlot 5, com renderizacao acelerada e sincronizacao de eixos.

---

## Funcionalidades Principais

* **Parser Nativo MoTeC (.ld e .ldx)**: Leitura direta dos arquivos binarios gerados pelo ACC sem intermediarios, decodificando cabecalhos, taxas de amostragem e canais de telemetria.
* **Eixo X Baseado em Distancia (Metros)**: Normalizacao automatica da telemetria integrando velocidade no tempo, permitindo comparacoes tecnicas precisas onde o espaco percorrido comeca estritamente em 0.0m.
* **Persistencia Ultrarrapida**: Banco de dados SQLite local embutido que carrega uma volta com milhares de pontos em menos de 3 milissegundos.
* **Graficos Sincronizados**:
  * Time Delta (Delta t): Comparativo de tempo ganho ou perdido metro a metro.
  * Velocidade: Curvas de velocidade em km/h.
  * Pedais: Acelerador e Freio de 0 a 100%.
  * Marchas: Grafico em degraus mostrando o escalonamento do cambio.
* **Navegacao Otimizada**: Pan e Zoom linkados entre todos os graficos a 60+ FPS, com trava horizontal exclusiva ao segurar `Ctrl`.
* **Comparacao de Voltas**: Suporte a sobreposicao entre a Volta Atual (Azul) e a Volta de Referencia (Laranja).
* **Mapa da Pista 2D com Posicao em Tempo Real**: Traçado gerado por odometria inercial a partir do sensor de guinada (`ROTY` do ACC) com fechamento topologico de circuito (-360 graus). Uma marcacao vermelha percorre a pista conforme o cursor se move pelos graficos.
* **Tooltip Flutuante Interativo**: Card dinamico que acompanha o mouse e compara os valores das duas voltas simultaneamente.
* **Filtros de Exibicao**: Controle individual para ocultar graficos e isolar a visualizacao apenas de acelerador ou freio.
* **Navegador de Sessoes**: Varredura automatica dos arquivos salvos na pasta de telemetria do jogo.

---

## Como Configurar o ACC para Gerar Telemetria

Para que o Assetto Corsa Competizione gere os arquivos lidos por esta aplicacao:

1. No jogo, entre na garagem (Setup do Carro).
2. Na aba **Electronics** (Eletronica), configure o campo **Telemetry Laps** para um valor maior que zero (ex: 10 ou 99).
3. Va para a pista e complete suas voltas.
4. Ao sair da sessao, o jogo salvara automaticamente os pares de arquivos `.ld` e `.ldx` no diretorio:
   ```text
   Documentos\Assetto Corsa Competizione\MoTeC