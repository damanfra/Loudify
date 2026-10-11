# Plano: equalização com modos prontos

Objetivo: tirar o som do "flat" com poucos cliques, usando 4 modos prontos escolhidos numa lista na janela de configurações.

## Como funciona

O Equalizer APO já tem filtros de equalização embutidos (`Filter:`). O Loudify escreve esses filtros no `loudify.txt`, **antes** do limitador. Assim:

- Não precisa de plugin novo nem de mais atraso: são filtros simples, como os do modo mais alto.
- O limitador continua segurando os picos, então realçar graves não estoura o som.
- Trocar de modo aplica na hora, como o ganho.

## Modos

Os valores são um ponto de partida para ajustar de ouvido. LS/HS = shelf grave/agudo (realça tudo abaixo ou acima da frequência), PK = pico (realça em torno da frequência).

| Modo | Filtros | Ideia |
|---|---|---|
| **Desligado** | — | Como hoje (padrão) |
| **Graves** | LS 100 Hz +6 dB · PK 250 Hz −1,5 dB | Grave encorpado, sem embolar o médio-grave |
| **Games** | PK 60 Hz +2 dB · PK 300 Hz −2 dB · PK 3 kHz +3 dB · HS 8 kHz +1,5 dB | Passos e tiros mais nítidos (2–4 kHz), impacto leve no grave |
| **Música** | LS 90 Hz +4 dB · PK 1 kHz −1,5 dB · HS 8 kHz +3 dB | Curva em "V": grave e brilho, médio mais leve |
| **Filme** | LS 70 Hz +4 dB · PK 2,5 kHz +3 dB · HS 10 kHz +1 dB | Explosões encorpadas e diálogos claros |

## Cuidados

- **Graves gastam espaço até o teto:** com graves realçados, o limitador trabalha mais e o volume médio pode cair um pouco. Por isso o modo *Graves* também tira um pouco de 250 Hz. Se precisar, o Loudify pode compensar automaticamente.
- **Junto com o modo mais alto (acima de +18 dB):** os filtros dos dois se somam. Para não exagerar, o modo mais alto pode reduzir o próprio corte de graves quando o modo *Graves* estiver ativo. A ajustar nos testes.
- **Cada fone soa diferente:** o mesmo modo pode ficar ótimo no Baseus e exagerado no G432. Uma melhoria possível depois: lembrar o modo por fone.

## Etapas

1. **Core:** os presets como dados, gerando as linhas `Filter:` no `loudify.txt`, com testes do texto gerado (como os do modo mais alto).
2. **Janela:** a lista "Equalização" (Desligado, Graves, Games, Música, Filme), aplicada na hora.
3. **Ajuste fino com você:** testar cada modo nos seus fones e corrigir os valores.
4. **Opcional, se pedir:** modo "Personalizado" com alguns controles (grave, médio, agudo) ou o modo lembrado por fone.
