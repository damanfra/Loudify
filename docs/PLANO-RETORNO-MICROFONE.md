# Plano: retorno do microfone (sidetone)

Objetivo: ouvir a própria voz no fone enquanto fala (jogos, Discord), com atraso pequeno o bastante para soar natural.

**Regra de corte:** se o atraso ficar parecido com o do recurso do Windows *Escutar este dispositivo* (Propriedades do microfone → Escutar), o recurso é descartado. Não vale a pena ter um segundo jeito de fazer a mesma coisa.

## Por que o "Escutar" do Windows tem atraso

O *Escutar* usa o caminho padrão de áudio compartilhado do Windows. O microfone e o fone trabalham em blocos de cerca de 10 ms cada, e ainda há buffers no driver USB ou Bluetooth. Na prática, o atraso fica na casa de algumas dezenas de milissegundos. A voz soa com eco, como se viesse de outra sala.

Para soar natural, o retorno precisa de **até ~10–15 ms**, do som entrar no microfone até sair no fone. Acima de ~20–25 ms o eco já incomoda.

## De onde dá para tirar atraso

| Etapa | Caminho padrão (Escutar) | Caminho de baixa latência |
|---|---|---|
| Bloco de captura do microfone | ~10 ms | ~3 ms (se o driver permitir) |
| Bloco de reprodução no fone (1–2 blocos) | ~10–20 ms | ~3–6 ms |
| Buffers do driver USB | alguns ms | alguns ms (não dá para mudar) |
| Limitador do Loudify (lookahead) | — | 1,5 ms |
| **Total estimado** | **dezenas de ms** | **~10–15 ms** |

A diferença vem de pedir ao Windows blocos menores, com a API WASAPI `IAudioClient3`, disponível desde o Windows 10. **Isso só funciona se o driver do fone aceitar blocos pequenos.** Se o driver só aceitar 10 ms, o total fica igual ao do *Escutar*, e a regra de corte se aplica.

O modo exclusivo do WASAPI não é uma opção: ele tomaria o microfone do Discord e dos jogos, ou o fone do resto do sistema.

## Limites conhecidos

- **Fone Bluetooth: inviável.** O Bluetooth sozinho já adiciona 100–200 ms ou mais, e o microfone em Bluetooth usa o modo de chamada (qualidade baixa). Se o QCY Melobuds estiver em Bluetooth, nem com dongle dá. Só funciona com fone USB ou com fio (P2).
- **Retorno por hardware é sempre melhor.** Alguns fones têm *sidetone* no próprio chip, com atraso zero. Se o fone tiver, use esse e descarte o do Loudify. Vale conferir no Logitech G HUB (opção de monitoramento do microfone/sidetone) se o G432 oferece, e no app do QCY.
- **O Equalizer APO processa tudo que vai para o fone.** O retorno passa pelo ganho do Loudify. Com +18 dB, a voz também sobe +18 dB, então o volume do retorno precisa compensar o ganho automaticamente.

## Fases

### Fase 0: viabilidade (decide se o recurso continua)

Pequena e barata. Nenhuma linha do recurso final é escrita antes dela.

1. **Conferir retorno por hardware** no G HUB e no app do QCY. Se existir, o plano termina aqui.
2. **Diagnóstico de período de áudio:** uma opção oculta no Loudify (ou um exe separado) lista, para cada microfone e fone, o período padrão e o mínimo que o driver aceita (`IAudioClient3::GetSharedModeEnginePeriod`).
   - Se o mínimo for ~10 ms nos dois lados → **descartar** (seria igual ao *Escutar*).
   - Se for ~3 ms ou menos → seguir.
3. **Medição real de ponta a ponta:** um protótipo liga o microfone ao fone com blocos mínimos. Encoste o microfone na concha do fone, o app toca um clique e mede quanto tempo ele leva para voltar pelo microfone. Também faz o teste A/B de ouvido com o *Escutar* do Windows.
   - **Critério para continuar:** ≤ 15 ms medidos **e** diferença clara de ouvido em relação ao *Escutar*. Caso contrário → descartar.

### Fase 1: motor de áudio (somente se a Fase 0 aprovar)

- Motor em C++ (como o limitador), embutido no `Loudify.exe`. C++ evita pausas do coletor de lixo do .NET no meio do áudio.
- Captura e reprodução em WASAPI compartilhado com `IAudioClient3` no período mínimo, orientado a eventos, numa thread com prioridade de áudio (MMCSS "Pro Audio").
- Buffer circular pequeno entre captura e reprodução: se atrasar, descarta amostras em vez de acumular atraso.
- Trata as diferenças entre microfone e fone: mono para estéreo, conversão de taxa se o microfone for 16 kHz e o fone 48 kHz, e pequenas diferenças de relógio.
- Testes automáticos do buffer e da conversão, como os do limitador.

### Fase 2: integração no app

- Liga/desliga na janela e no menu do ícone, com atalho próprio (como o Ctrl+Alt+L).
- Volume do retorno, compensando automaticamente o ganho do Loudify.
- Filtro passa-altas (~100 Hz) para cortar ruído grave e respiração, e um *noise gate* leve opcional.
- Escolha do microfone e do fone, com padrão no dispositivo padrão do Windows. Volta sozinho se o fone for desconectado e reconectado.
- Mostra a latência medida na janela, para você saber o que está ouvindo.

### Fase 3: ajuste fino

- Uso real em jogos e no Discord: conferir que o microfone continua normal para os outros apps e que não há estalos.
- Ajustar o tamanho do buffer (o equilíbrio entre estalos e atraso) e os padrões de volume e filtro.

## Resumo das decisões

| Situação | Decisão |
|---|---|
| Fone tem sidetone por hardware | Usar o do fone, descartar o do Loudify |
| Fone Bluetooth | Descartar |
| Driver só aceita blocos de ~10 ms | Descartar (igual ao *Escutar*) |
| Medição > 15 ms ou sem diferença de ouvido | Descartar |
| Medição ≤ 15 ms e diferença clara | Implementar Fases 1–3 |

Próximo passo: Fase 0. Ela precisa de testes no seu PC: o diagnóstico de período e a medição com o microfone encostado no fone.
