# Melee Goblin BT (Sample)

1. Create a Monster prefab (from your Core workflow).
2. Ensure the prefab will add `ControllerMonster` at runtime (Core `Monster` does this automatically).
3. Add `GGemCo2DAiBt.MonsterBtRunner` component to the same GameObject.
4. Create a `MonsterBehaviorTreeAsset` via `Create > GGemCo > AI > Monster Behavior Tree`.
5. Select the asset and click **Create Example BT** in the Inspector.
6. Assign the asset to the Runner.

## Notes
- Execution is delegated to Core via `GGemCo2DCore.IMonsterCombatDriver`.
- By default, `ControllerMonster` implements that interface.
