# First-run inventory and calibration architecture

## User flow

1. On first launch the owner selects the hardware they actually own: Joy-Con pair, PS Move pair, Android phone, Wii Balance Board and optional hand tracking. Quest 3 is the fixed headset contract.
2. The inventory is stored in the per-user config root. It can be reopened from **Cihazlarım** at any time.
3. The profile catalog generates every non-empty subset of the selected sensor families plus **Normal VR**. With all four sensor families this produces 15 NiiMotion combinations and Normal VR.
4. Profiles are ordered from lower setup effort to higher fusion complexity; expected movement performance breaks ties. Phone-only and board-only modes remain visibly experimental.
5. A locomotion profile cannot launch until every sensor family it requires has completed the three base calibration phases.

## Calibration layers

### Device base calibration

Every selected sensor family has its own connection screen and three sequential five-minute phases:

- phase 1: mounting/neutral plus slow controlled movement;
- phase 2: natural gait, stopping and restarting;
- phase 3: pace range and non-walking rejection motions.

Each phase writes raw JSONL streams and a versioned manifest. A phase is accepted only after adequate continuous samples are recorded from every required side/source. Cancellation, disconnect or insufficient data leaves progress unchanged and incomplete files are removed.

### Active-profile walking calibration

After each required device is ready, every multi-device profile gets its own three-phase synchronized recording. Each combined phase lasts two minutes (six minutes total); individual device base phases remain five minutes. The Calibration Center exposes the available combinations in one compact selector. Every active stream receives the same session/phase identity and a profile manifest links the results. The offline pipeline turns three accepted phases into `config/profile-fusion/<profile-id>.json`, containing capture quality, timing tolerance, disagreement grace and phone/board agreement weights without overwriting device calibration. Multi-device locomotion and game launch fail closed until both the three-phase progress and this local model are present.

### Optional model improvement

Legacy long-form labs remain available under **Modeli geliştir**. These recordings are explicitly optional and do not block first use. The user chooses which device model to strengthen.

## Runtime combinations

- Joy-Con, phone and board combinations continue through the established fusion engine.
- PS Move can run alone, with phone, with board or with both.
- Joy-Con + PS Move combinations run both personalized leg engines. Matching cadence is required to start; a learned short grace window prevents momentary sensor disagreement from producing abrupt dropouts.
- Optional phone and board sources cannot create leg gait in leg-sensor profiles. Board loss/contact and turn guards remain fail-closed.
- Every required connection loss zeros output and ends the session safely.

## Personal data and storage

Inventory and calibration progress are per-user files and are excluded from source control. Raw calibration lives under the bounded NiiMotion data root. Failed partial sessions are deleted immediately; successful raw sessions and manifests are retained for replay, future model updates and migration.
