# Loudify

Aplicativo pessoal para Windows 10 e 11 que aumenta o volume do áudio do sistema além de 100%, como o FxSound, sem distorcer e sem adicionar atraso perceptível em jogos.

## Como funciona

```
Jogos / Discord / navegador
        │
   Mixer do Windows
        │
   Equalizer APO  ──►  Loudify Limiter (VST)  ──►  Fone / headset
        ▲                ganho + limitador
        │
   Loudify.exe (ícone na bandeja: ganho, liga/desliga, atalho)
```

- **[Equalizer APO](https://sourceforge.net/projects/equalizerapo/)** (gratuito) é instalado no caminho do áudio do Windows. Ele processa o som de todos os apps sem driver virtual e sem atraso extra.
- **Loudify Limiter** é um plugin VST próprio (`native/LoudifyLimiter`) que aplica o ganho e, em seguida, um limitador de picos com *lookahead* de 1,5 ms. Aumentar o volume digitalmente distorce quando o sinal passa de 0 dBFS; o limitador reduz o ganho só nos picos, então o som fica mais alto sem estourar.
- **Loudify.exe** fica na área de notificação. Ele grava `loudify.txt` na pasta de configuração do Equalizer APO, que recarrega a configuração na hora. Não precisa rodar como administrador.

## Instalação

1. **Instale o Equalizer APO** (versão 64 bits, 1.3 ou mais recente).
   - No *Configurator* que abre no fim da instalação, marque em *Playback devices* a linha do fone ou headset que você usa (ex.: *Alto-falantes — Logitech G432 Gaming Headset* ou *Fones de ouvido — QCY Melobuds ANC Stereo*). Se usa mais de um, marque todos. Não marque microfones (*Capture devices*) nem linhas "Hands-Free"/"Headset" de fones Bluetooth, que são o modo de chamada.
   - Se depois o volume não mudar, abra o Configurator de novo (Menu Iniciar → Equalizer APO → Configurator), marque *Troubleshooting options* no fone e teste as opções de instalação alternativas.
   - Reinicie o Windows.
2. **Baixe o `Loudify.exe`** em *Releases* (última versão), ou em *Actions* → último build verde → artefato `Loudify-win-x64`.
3. **Execute o `Loudify.exe`.** Na primeira execução o Windows SmartScreen pode avisar que o app não é assinado: clique em *Mais informações* → *Executar assim mesmo*.

Na primeira vez, o Loudify adiciona `Include: loudify.txt` ao `config.txt` do Equalizer APO. Se o `config.txt` for o padrão da instalação (que **reduz** o volume em 6 dB), ele é substituído; o original fica salvo em `config.txt.loudify-backup`.

## Uso

| Ação | Como |
|---|---|
| Ligar / desligar | **Ctrl+Alt+L** (funciona dentro do jogo), ou clique direito no ícone → *Ligado* |
| Ajustar o ganho | Clique no ícone → controle deslizante (0 a +24 dB, aplicado na hora) |
| Trocar o atalho | Janela de configurações → clique no campo *Atalho* e pressione a nova combinação |
| Iniciar com o Windows | Janela de configurações |
| Abaixar o jogo enquanto fala | Janela de configurações → *Abaixar o jogo enquanto eu falo* |
| Atualizar | Janela de configurações → botão *Verificar atualizações* (vira *Atualizar* quando há versão nova), ou clique direito no ícone |

- **Modo mais alto (acima de +18 dB):** a partir daí quase todo o som já encosta no teto digital, e mais ganho só comprime. Por isso, entre +18 e +24 dB o Loudify ativa aos poucos um realce de presença (+4 dB em 3 kHz, onde o ouvido é mais sensível), tira um pouco de grave (−3 dB abaixo de 120 Hz, que ocupa espaço até o teto) e sobe o teto do limitador de −1 para −0,1 dB. Em +21 dB o modo está em 50%; em +24 dB, em 100%. A partir de +18 dB a janela de configurações mostra o ganho em vermelho, com um aviso de que pode haver distorção.
- **Abaixar o jogo enquanto eu falo:** para headsets com retorno de voz próprio (sidetone), como o Logitech G432. Com o jogo amplificado, o sidetone fica baixo demais em comparação. Com essa opção, o Loudify escuta o microfone de comunicação do Windows e, quando você fala, baixa o jogo alguns dB (padrão: 8 dB). Quando você para, volta ao normal em menos de 1 s. A sua voz continua vindo do headset, sem atraso. Ajuste a *sensibilidade* olhando o medidor: ele deve passar da marca só quando você fala. O microfone é aberto em modo compartilhado, então o Discord e os jogos continuam usando normalmente. O Windows mostra o ícone de microfone em uso enquanto a opção está ligada. Se o Loudify não conseguir abrir o microfone, confira em *Configurações → Privacidade → Microfone* se aplicativos da área de trabalho podem acessá-lo.
- Um som curto confirma ao ligar (tom subindo) e desligar (tom descendo), útil em tela cheia. Pode ser desativado.
- Ícone azul = ligado, cinza = desligado, vermelho = erro (passe o mouse para ver o motivo).
- **Fechar o Loudify desliga o aumento.** Ao abrir de novo, o último estado é restaurado.
- As configurações ficam em `%AppData%\Loudify\settings.json`.
- O Loudify verifica se há versão nova toda vez que abre. Se houver, avisa com uma notificação e o botão *Verificar atualizações* vira *Atualizar*. Ao atualizar, ele baixa a nova versão de *Releases*, confere o SHA-256, substitui o `Loudify.exe` e reabre.

> **Cuidado com a audição:** +24 dB é um aumento enorme (cerca de 16 vezes a amplitude). Quanto mais sensível o fone, menos ganho ele precisa: comece com pouco (+6 a +10 dB) e aumente aos poucos.

## Desinstalar

Feche o Loudify (o aumento é desligado) e apague o `Loudify.exe`. Para remover tudo: tire a linha `Include: loudify.txt` do `config.txt` do Equalizer APO (ou restaure o backup), apague a pasta `config\Loudify` e, se quiser, desinstale o Equalizer APO.

## Desenvolvimento

Estrutura:

- `native/LoudifyLimiter`: plugin VST 2 em C++ (sem dependências; o CRT é estático porque a DLL é carregada pelo `audiodg.exe`).
- `src/Loudify.Core`: geração da configuração, configurações e atalhos (.NET 8, multiplataforma, testável).
- `src/Loudify`: app de bandeja WinForms (.NET 8, Windows). A DLL do limitador é embutida no exe.
- `tests/Loudify.Core.Tests`: testes xUnit.

Build local no Windows (Visual Studio 2022 com C++ e .NET 8):

```powershell
cmake -S native/LoudifyLimiter -B native/LoudifyLimiter/build -A x64
cmake --build native/LoudifyLimiter/build --config Release
ctest --test-dir native/LoudifyLimiter/build -C Release
dotnet test tests/Loudify.Core.Tests
dotnet publish src/Loudify/Loudify.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

GitHub Actions:

- **Build** (`.github/workflows/build.yml`): faz o mesmo em todo push e pull request (exceto na `main`) e guarda o `Loudify.exe` como artefato. O botão *Run workflow* só gera o build, não uma release.
- **Release** (`.github/workflows/release.yml`): roda em todo push na `main` e pelo botão *Actions → Release → Run workflow* (somente com a `main` selecionada). Compila, testa e publica o `Loudify.exe` em *Releases* com a tag `v<versão>`. Sem versão informada, usa `<Version do Loudify.csproj>.<nº do run>` (ex.: `v0.1.0.7`); para uma versão "limpa", digite-a no botão (ex.: `0.2.0`). Depois de uma versão manual, suba o `<Version>` do `Loudify.csproj` (ex.: `0.2.0` → `0.2.1`), senão as releases automáticas seguintes (`0.1.0.N`) ficam com número menor e o app não as oferece como atualização.

## Próxima etapa: sidetone (retorno do microfone)

Planejado. O sidetone por software precisa de atraso total abaixo de ~10–15 ms para soar natural; se ficar igual ao *Escutar este dispositivo* do Windows, é descartado. Plano completo, com as fases e os critérios de corte: [`docs/PLANO-RETORNO-MICROFONE.md`](docs/PLANO-RETORNO-MICROFONE.md).
