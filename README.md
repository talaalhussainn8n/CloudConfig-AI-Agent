# CloudConfig-Agent: KI-Agent für sichere AWS-Konfigurationen

![CI](https://github.com/talaalhussainn8n/CloudConfig-AI-Agent/actions/workflows/ci.yml/badge.svg)

Ein C#-Agent, der die Konfiguration eines AWS-Kontos prüft, Risiken nach Schweregrad ordnet und konkrete Korrekturbefehle vorschlägt.
Fragen wie *"Was sollte ich zuerst beheben?"* beantwortet ein Sprachmodell, das sich die nötigen Fakten **selbst über Werkzeuge holt** (Tool Calling).

## Funktionen

- **Live-Analyse über das AWS SDK for .NET** (S3, EC2) mit reinen Leserechten (`SecurityAudit`), oder offline aus einer gespeicherten Momentaufnahme.
- **Regelwerk** mit nachvollziehbaren Prüfungen:

| Regel | Prüfung | Schweregrad |
| --- | --- | --- |
| S3-001 | Block Public Access aktiv | Hoch |
| S3-002 | Standardverschlüsselung aktiv | Mittel |
| S3-003 | Versionierung aktiv | Niedrig |
| EC2-001 | Kein SSH, RDP oder Datenbankport offen für das Internet | Kritisch / Hoch |
| EBS-001 | Volumes verschlüsselt | Mittel |

- **Agent mit Werkzeugen** `list_resources`, `run_compliance_checks`, `get_resource`. Der Agent entscheidet selbst, welche er aufruft, und antwortet erst danach.
- **Sicherheitsleitplanken**: Werkzeuge sind nur lesend, der Agent schlägt Befehle vor, führt aber nichts aus; maximale Schrittzahl gegen Endlosschleifen.
- **Ohne Sprachmodell nutzbar**: dann erzeugt das Programm einen strukturierten Markdown-Bericht.
- **Betrieb als AWS Lambda** mit täglicher Prüfung (SAM-Vorlage `template.yaml`), oder als CLI in CI-Pipelines (Exit-Code 2 bei kritischen Befunden).

## Ablauf des Agenten

```
Frage --> Sprachmodell --(tool_call: run_compliance_checks)--> Regelwerk --> Befunde
               ^                                                               |
               +----------------------- Ergebnis als "tool"-Nachricht ---------+
               |
               +--> Antwort mit Priorisierung und Korrekturbefehlen
```

## Projekte

| Projekt | Inhalt |
| --- | --- |
| `src/CloudConfigAgent.Core` | Modell, Regelwerk, AWS- und JSON-Datenquellen, Agent und Werkzeuge |
| `src/CloudConfigAgent.Cli` | Kommandozeilenprogramm |
| `src/CloudConfigAgent.Lambda` | AWS-Lambda-Funktion für geplante Prüfungen |
| `tests/CloudConfigAgent.Tests` | Tests für Regeln, Werkzeuge und den Agenten-Ablauf (mit simuliertem Sprachmodell) |

## Nutzung

```bash
# Beispielkonto ohne AWS-Zugang und ohne Sprachmodell
dotnet run --project src/CloudConfigAgent.Cli -- --snapshot samples/snapshot.json

# Eigenes Konto (nutzt die üblichen AWS-Zugangsdaten), mit Sprachmodell
export LLM_BASE_URL=https://api.openai.com/v1
export LLM_API_KEY=...
dotnet run --project src/CloudConfigAgent.Cli -- --aws --region eu-central-1 --question "Welche Datenbanken sind aus dem Internet erreichbar?"
```

Bereitstellung als Lambda:

```bash
sam build && sam deploy --guided
```

## Tests

```bash
dotnet test tests/CloudConfigAgent.Tests
```

Der Agenten-Ablauf wird mit einem simulierten Sprachmodell getestet: Werkzeugaufruf, Rückgabe des Ergebnisses mit passender `tool_call_id`, Abbruch nach maximaler Schrittzahl.

## Mögliche Erweiterungen

- Weitere Dienste: IAM (MFA, alte Zugriffsschlüssel), RDS, CloudTrail
- Mehrere Regionen und Konten (AWS Organizations)
- Ergebnisse als AWS Security Hub Findings melden
