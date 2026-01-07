using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class MonsterEncounterManager : MonoBehaviour
{
    [Header("Threat")]
    [SerializeField] private long threat = 0;
    [SerializeField] private long threatThreshold = 100;
    [SerializeField] private long threatAfterEncounter = 0;

    [Header("Semi-Random Threat Gain")]
    [SerializeField] private Vector2 tickIntervalRange = new Vector2(1.0f, 3.5f);
    [SerializeField] private Vector2Int threatGainRange = new Vector2Int(5, 18);

    [Header("Spawn")]
    [SerializeField] private GameObject[] monsterPrefabs;

    [Tooltip("LEGACY: Used only if Monster Spawn Points is empty.")]
    [SerializeField] private Transform spawnPoint;

    [Tooltip("Preferred: Spawn slots for multi-monster encounters.")]
    [SerializeField] private Transform[] monsterSpawnPoints;

    [SerializeField] private bool requireNoActiveMonster = true;

    [Header("Multi-Monster Encounter")]
    [SerializeField] private int minMonstersPerEncounter = 1;
    [SerializeField] private int maxMonstersPerEncounter = 3;

    [Header("Combat")]
    [SerializeField] private int heroAttackFlatBonus = 0;

    [Header("Blocking")]
    [SerializeField] private bool holdRightClickToBlock = true;
    [SerializeField] private string playerIdleBlockStateName = "Idle Block";
    [SerializeField] private string playerBlockImpactStateName = "Block";
    [SerializeField] private string playerIdleStateName = "Idle";
    [SerializeField] private float idleBlockCrossfadeTime = 0.05f;

    [Header("Monster Attacks")]
    [SerializeField] private bool enableMonsterAttacks = true;
    [SerializeField] private Vector2 baseMonsterAttackIntervalRange = new Vector2(2.0f, 3.5f);
    [SerializeField] private float baseMonsterTelegraphDuration = 0.45f;

    [Header("Baseline Stats")]
    [SerializeField] private int baselineMonsterAttackRate = 10;
    [SerializeField] private int baselineMonsterSpeed = 10;

    [Header("Attack Rate Clamp")]
    [SerializeField] private float minAttackRateMultiplier = 0.5f;
    [SerializeField] private float maxAttackRateMultiplier = 2.0f;

    [Header("Monster Telegraph Timing")]
    [Range(0.05f, 0.95f)]
    [SerializeField] private float lungeStartFraction = 0.55f;

    [Header("Rewards")]
    [SerializeField] private int xpPerKill = 5;
    [SerializeField] private Vector2Int goldDropRange = new Vector2Int(1, 5);

    [Header("Damage Numbers")]
    [SerializeField] private DamageNumber damageNumberPrefab;
    [SerializeField] private Vector3 damageNumberWorldOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private Vector3 damageNumberRandomJitter = new Vector3(0.2f, 0.15f, 0f);

    [Header("References")]
    [SerializeField] private TopStatusBar topStatusBar;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private ScrollingBackground scrollingBackground;
    [SerializeField] private StretchController stretchController;
    [SerializeField] private HeroStats heroStats;

    [Header("Player State Names")]
    [SerializeField] private string playerRunStateName = "Run";
    [SerializeField] private string[] playerAttackStateNames = { "Attack1", "Attack2", "Attack3" };

    [Header("Timing")]
    [SerializeField] private float attackResolveDelay = 0.25f;

    [Header("Fallback")]
    [SerializeField] private int defaultMonsterDamage = 8;

    private int _idleStateHash;
    private int _idleBlockStateHash;
    private int _blockImpactStateHash;

    private readonly List<Monster> _activeMonsters = new();
    private readonly Dictionary<Monster, Coroutine> _monsterAttackLoops = new();

    private Monster _currentTarget;
    private Coroutine _threatLoop;

    private bool _resolvingAttack;
    private bool _blockHeld;
    private bool _blockImpactPlaying;
    private bool _blockedThisMonsterAttack;

    private float _blockImpactClipLength;
    private Camera _mainCam;

    private void Awake()
    {
        _mainCam = Camera.main;

        if (heroStats == null)
            heroStats = FindFirstObjectByType<HeroStats>();

        if (topStatusBar == null)
            topStatusBar = FindFirstObjectByType<TopStatusBar>();

        _idleStateHash = Animator.StringToHash(playerIdleStateName);
        _idleBlockStateHash = Animator.StringToHash(playerIdleBlockStateName);
        _blockImpactStateHash = Animator.StringToHash(playerBlockImpactStateName);

        CacheBlockImpactClipLength();
    }

    private void OnEnable()
    {
        _threatLoop = StartCoroutine(ThreatLoop());
        PushUI();
    }

    private void OnDisable()
    {
        if (_threatLoop != null) StopCoroutine(_threatLoop);
        StopAllMonsterAttackLoops();
    }

    private void Update()
    {
        UpdateBlockHeld();
        DrainStaminaForBlocking(Time.deltaTime);

        if (heroStats != null)
            heroStats.Tick(ComputeIdleForRegen(), Time.deltaTime);

        MaintainIdleBlockState();

        if (_activeMonsters.Count > 0 && !_resolvingAttack && !_blockImpactPlaying)
        {
            if (Input.GetMouseButtonDown(0) &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                Monster clicked = TryGetClickedMonster();
                if (clicked != null && _activeMonsters.Contains(clicked) && !clicked.IsDead)
                {
                    if (heroStats == null || heroStats.HasStamina(heroStats.StaminaCostPerAttack))
                        OnMonsterClicked(clicked);
                }
            }
        }

        if (_currentTarget != null && _currentTarget.IsDead)
            _currentTarget = GetFirstAliveMonster();

        PushUI();
    }

    // ---------------- BLOCKING (CLASS SAFE) ----------------

    private void UpdateBlockHeld()
    {
        if (!holdRightClickToBlock || heroStats == null || !heroStats.CanBlock)
        {
            _blockHeld = false;
            return;
        }

        if (_activeMonsters.Count == 0)
        {
            _blockHeld = false;
            return;
        }

        _blockHeld = Input.GetMouseButton(1);
    }

    private void DrainStaminaForBlocking(float dt)
    {
        if (!_blockHeld || heroStats == null || _activeMonsters.Count == 0)
            return;

        if (!heroStats.TryDrainStaminaWhileBlocking(dt))
            _blockHeld = false;
    }

    // ---------------- ATTACK RESOLVE ----------------

    public void OnMonsterClicked(Monster monster)
    {
        if (monster == null || _resolvingAttack || _blockImpactPlaying)
            return;

        if (!heroStats.TryConsumeAttackStamina())
            return;

        _currentTarget = monster;
        StartCoroutine(ResolveAttack(monster));
    }

    private IEnumerator ResolveAttack(Monster monster)
    {
        _resolvingAttack = true;

        PlayRandomAttack();
        yield return new WaitForSeconds(attackResolveDelay);

        if (monster == null || monster.IsDead)
        {
            _resolvingAttack = false;
            yield break;
        }

        int dmg = Mathf.Max(0, heroStats.Attack + heroAttackFlatBonus);
        int dealt = monster.TakeDamage(dmg);

        SpawnDamageNumber(monster.transform.position, dealt);

        // 🔥 CAMPFIRE ITEM / PERK HOOK
        heroStats.ApplyOnHitEffectsTo(monster);

        if (monster.IsDead)
        {
            heroStats.GainXP(xpPerKill);
            heroStats.AddGold(Random.Range(goldDropRange.x, goldDropRange.y + 1));

            float fx = monster.PlayDeathEffects();
            if (fx > 0f) yield return new WaitForSeconds(fx);

            RemoveMonster(monster);
        }

        _resolvingAttack = false;
    }

    private void RemoveMonster(Monster monster)
    {
        _activeMonsters.Remove(monster);

        if (_monsterAttackLoops.TryGetValue(monster, out var loop))
        {
            StopCoroutine(loop);
            _monsterAttackLoops.Remove(monster);
        }

        Destroy(monster.gameObject);

        _currentTarget = GetFirstAliveMonster();

        if (_activeMonsters.Count == 0)
            EndEncounter();
    }

    // ---------------- ENCOUNTER FLOW ----------------

    private void EndEncounter()
    {
        StopAllMonsterAttackLoops();
        _blockHeld = false;
        _blockImpactPlaying = false;

        stretchController?.SetEncounterActive(false);

        if (stretchController == null || stretchController.CanResumeRunningVisuals())
        {
            scrollingBackground?.SetPaused(false);
            playerAnimator?.CrossFadeInFixedTime(playerRunStateName, 0.05f, 0);
        }
    }

    // ---------------- UTIL ----------------

    private Monster GetFirstAliveMonster()
    {
        foreach (var m in _activeMonsters)
            if (m != null && !m.IsDead) return m;
        return null;
    }

    private void StopAllMonsterAttackLoops()
    {
        foreach (var loop in _monsterAttackLoops.Values)
            if (loop != null) StopCoroutine(loop);

        _monsterAttackLoops.Clear();
    }

    private bool ComputeIdleForRegen()
    {
        return _activeMonsters.Count > 0 &&
               !_blockHeld &&
               !_resolvingAttack &&
               !_blockImpactPlaying;
    }

    private void MaintainIdleBlockState()
    {
        if (!heroStats.CanBlock || playerAnimator == null || _activeMonsters.Count == 0)
            return;

        if (_blockImpactPlaying) return;

        int target = _blockHeld ? _idleBlockStateHash : _idleStateHash;
        playerAnimator.CrossFadeInFixedTime(target, idleBlockCrossfadeTime, 0);
    }

    private void PlayRandomAttack()
    {
        if (playerAnimator == null || playerAttackStateNames.Length == 0) return;
        if (_blockHeld || _blockImpactPlaying) return;

        string anim = playerAttackStateNames[Random.Range(0, playerAttackStateNames.Length)];
        playerAnimator.CrossFadeInFixedTime(anim, 0f, 0);
    }

    private Monster TryGetClickedMonster()
    {
        if (_mainCam == null) return null;

        Vector3 world = _mainCam.ScreenToWorldPoint(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(world, Vector2.zero);
        return hit.collider ? hit.collider.GetComponentInParent<Monster>() : null;
    }

    private void CacheBlockImpactClipLength() { /* unchanged */ }

    private void SpawnDamageNumber(Vector3 pos, int amount) { /* unchanged */ }

    private void PushUI() { /* unchanged */ }

    private IEnumerator ThreatLoop() { /* unchanged */ yield break; }

    private void SpawnEncounterMonsters() { /* unchanged */ }
}
