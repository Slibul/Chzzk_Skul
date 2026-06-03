$content = Get-Content -Raw "ChzzkGameMode.cs"

# Fix DoNPC
$doNpcReplacement = @'
        private void DoNPC(string nickname)
        {
            Character player = GetPlayer();
            if (player == null) return;

            string[] npcNames = { "Field_Fox", "Field_Ogre", "MagicalSlime", "DarkPriest", "HalflingGirl", "Field_DeathKnight" };
            string chosenNpcName = npcNames[_random.Next(npcNames.Length)];

            var allObjects = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>();
            GameObject npcPrefab = allObjects.FirstOrDefault(g => g.name.Contains(chosenNpcName) && g.scene.rootCount == 0);

            if (npcPrefab == null)
            {
                npcPrefab = allObjects.FirstOrDefault(g => (g.name.Contains(chosenNpcName) || g.name.Contains("Field_") || g.name.Contains("Npc")) && g.GetComponent<UnityEngine.Collider2D>() != null);
            }

            if (npcPrefab != null)
            {
                UnityEngine.Object.Instantiate(npcPrefab, player.transform.position + UnityEngine.Vector3.right * 2f, UnityEngine.Quaternion.identity);
                ShowFloatingText($"{nickname}님이 NPC를 소환했습니다! ({npcPrefab.name})");
            }
            else
            {
                ShowFloatingText($"{nickname}님이 부를 NPC가 없어 꽝이 되었습니다!");
                DoBuffOrCurse(nickname);
            }
        }
'@

$content = [System.Text.RegularExpressions.Regex]::Replace($content, '(?s)private void DoNPC\(string nickname\)\s*\{.*?(?=        private void DoFindNPC)', $doNpcReplacement + "`r`n`r`n")

# Fix DoBoss
$doBossReplacement = @'
        private void DoBoss(string nickname)
        {
            Character player = GetPlayer();
            if (player == null) return;

            string[] bossNames = { "Yggdrasil", "Leiana", "Chimera", "StData", "FirstHero", "Boss" };
            string chosenBossName = bossNames[_random.Next(bossNames.Length)];

            var allObjects = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.GameObject>();
            GameObject bossPrefab = allObjects.FirstOrDefault(g => (g.name.Contains(chosenBossName) || g.name.Contains("Boss")) && g.scene.rootCount == 0 && g.GetComponent<Character>() != null);

            if (bossPrefab != null)
            {
                try {
                    UnityEngine.Object.Instantiate(bossPrefab, player.transform.position + UnityEngine.Vector3.right * 3f, UnityEngine.Quaternion.identity);
                    ShowFloatingText($"{nickname}님이 억지로 보스를 소환했습니다! ({bossPrefab.name})");
                    return;
                } catch {
                    // Ignore crash/error during instantiate and fallback
                }
            }

            // Fallback to old behavior if prefab fails
            bool minibossSpawned = false;
            var enemies = UnityEngine.Object.FindObjectsOfType<Character>().Where(c => c.type == Character.Type.TrashMob).ToList();
            if (enemies.Count > 0)
            {
                var target = enemies[_random.Next(enemies.Count)];
                if (target != null && target.health != null && target.stat != null)
                {
                    try {
                        target.stat.AttachValues(new Stat.Values(new Stat.Value(Stat.Category.PercentPoint, Stat.Kind.Health, 5f)));
                        target.stat.AttachValues(new Stat.Values(new Stat.Value(Stat.Category.PercentPoint, Stat.Kind.AttackDamage, 1.5f)));
                    } catch { }

                    target.health.Heal(999999);
                    minibossSpawned = true;
                }
            }
            
            ForceNextDarkEnemy = true;
            
            if (minibossSpawned)
                ShowFloatingText($"{nickname}님이 몬스터 하나를 미니보스로 둔갑시켰습니다!");
            else
                ShowFloatingText($"{nickname}님이 보스를 소환하여 다음 맵에 적들이 강해집니다!");
        }
'@

$content = [System.Text.RegularExpressions.Regex]::Replace($content, '(?s)private void DoBoss\(string nickname\)\s*\{.*?(?=        #endregion Command Handlers)', $doBossReplacement + "`r`n`r`n")

[System.IO.File]::WriteAllText("ChzzkGameMode.cs", $content, [System.Text.Encoding]::UTF8)
