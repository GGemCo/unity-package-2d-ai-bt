# GGemCo 2D AI - Behavior Tree

- This package provides a data-driven Behavior Tree system for monsters.
- **Decision** happens in this package.
- **Execution** (movement / attack / animation) stays in **GGemCo 2D Core** via `GGemCo2DCore.IMonsterCombatDriver`.

## Quick start
1. Install `com.ggemco.2d.core`.
2. Add `MonsterBtRunner` to a Monster prefab.
3. Create a `MonsterBehaviorTreeAsset` and assign it to the runner.
4. Import the sample (Package Manager > Samples) for a ready-to-use example.
