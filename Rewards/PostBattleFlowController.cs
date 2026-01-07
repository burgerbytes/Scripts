// PATH: Assets/Scripts/Traversal/PostBattleFlowController.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orchestrates the post-battle flow:
/// - Scroll the background by a segment proportional to BattlesPerStretch
/// - Show rewards (choose one item)
/// - Show prep menu (Inventory / Deck / Continue)
/// - When player continues, advance stretch via StretchController.CompleteBattleAndAdvance()
/// </summary>
public class PostBattleFlowController : MonoBehaviour
{
    [Header("Reward Options")]
    [Header("Refs")]
    [SerializeField] private StretchController stretch;
    [SerializeField] private ScrollingBackground scrollingBackground;
    [SerializeField] private PostBattleRewardPanel rewardPanel;
    [SerializeField] private PostBattlePrepPanel prepPanel;
    [SerializeField] private PlayerInventory inventory;

    [Header("Reward Options")]
    [Tooltip("Pool of item reward options to roll from.")]
    [SerializeField] private List<ItemOptionSO> itemOptionPool = new List<ItemOptionSO>();
    // ADD THIS METHOD (so BattleManager can reuse the same pool on startup)
    public List<ItemOptionSO> GetItemOptionPool()
    {
        return itemOptionPool;
    }

    [Tooltip("Min number of reward choices shown.")]
    [SerializeField] private int minRewardChoices = 1;

    [Tooltip("Max number of reward choices shown.")]
    [SerializeField] private int maxRewardChoices = 5;

    [Tooltip("If true, the same option may appear multiple times when the pool is small.")]
    [SerializeField] private bool allowDuplicateChoices = false;

    [Header("Background Progress Scroll")]
    [Tooltip("Total world-distance to scroll between the first fight and the campfire for an entire stretch.")]
    [SerializeField] private float totalTravelDistanceToCampfire = 30f;

    [Tooltip("Clamp the computed scroll duration (seconds).")]
    [SerializeField] private Vector2 scrollDurationClamp = new Vector2(0.35f, 1.75f);

    [Tooltip("If true, scroll segment plays while the reward panel is shown.")]
    [SerializeField] private bool scrollDuringRewards = true;

    [Tooltip("If true, show the prep panel even after the final fight before campfire. If false, skip prep and go to campfire.")]
    [SerializeField] private bool showPrepBeforeCampfire = false;

    public bool IsRunning { get; private set; }

    private ItemOptionSO _chosenReward;
    private bool _rewardChosen;
    private bool _continuePressed;

    private void Awake()
    {
        if (stretch == null)
            stretch = FindFirstObjectByType<StretchController>();

        if (scrollingBackground == null)
            scrollingBackground = FindFirstObjectByType<ScrollingBackground>();

        if (inventory == null)
            inventory = FindFirstObjectByType<PlayerInventory>();
    }

    public void BeginPostBattleSequence()
    {
        if (IsRunning) return;
        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        IsRunning = true;
        _rewardChosen = false;
        _continuePressed = false;
        _chosenReward = null;

        // Segment scroll setup
        if (scrollingBackground != null && stretch != null)
        {
            float perBattle = Mathf.Approximately(totalTravelDistanceToCampfire, 0f)
                ? 0f
                : totalTravelDistanceToCampfire / Mathf.Max(1, stretch.BattlesPerStretch);

            float speed = Mathf.Max(0.01f, scrollingBackground.ScrollSpeed);
            float duration = Mathf.Abs(perBattle) / speed;
            duration = Mathf.Clamp(duration, scrollDurationClamp.x, scrollDurationClamp.y);

            if (scrollDuringRewards)
                scrollingBackground.PlayScrollSegment(perBattle, duration);
            else
                scrollingBackground.PlayScrollSegment(perBattle, duration);
        }

        // Rewards
        if (rewardPanel != null && itemOptionPool != null && itemOptionPool.Count > 0)
        {
            var rolled = RollRewardChoices();
            rewardPanel.Show(rolled, OnRewardChosen);
            yield return new WaitUntil(() => _rewardChosen);
            rewardPanel.Hide();
        }
        else
        {
            _rewardChosen = true;
        }

        // Apply reward
        if (_chosenReward != null && inventory != null && _chosenReward.item != null)
            inventory.Add(_chosenReward.item, _chosenReward.quantity);

        // Determine if we are about to complete stretch (after this battle).
        bool thisBattleCompletesStretch = false;
        if (stretch != null)
            thisBattleCompletesStretch = (stretch.BattlesCompleted + 1) >= stretch.BattlesPerStretch;

        if (thisBattleCompletesStretch && !showPrepBeforeCampfire)
        {
            // Advance directly to campfire
            if (stretch != null)
                stretch.CompleteBattleAndAdvance();

            IsRunning = false;
            yield break;
        }

        // Prep menu
        if (prepPanel != null && stretch != null)
        {
            prepPanel.Show(stretch.BattlesCompleted, stretch.BattlesPerStretch, OnContinuePressed);
            yield return new WaitUntil(() => _continuePressed);
            prepPanel.Hide();
        }
        else
        {
            _continuePressed = true;
        }

        // Advance to next battle / campfire
        if (stretch != null)
            stretch.CompleteBattleAndAdvance();

        IsRunning = false;
    }

    private void OnRewardChosen(ItemOptionSO chosen)
    {
        _chosenReward = chosen;
        _rewardChosen = true;
    }

    private void OnContinuePressed()
    {
        _continuePressed = true;
    }

    private List<ItemOptionSO> RollRewardChoices()
    {
        int poolCount = itemOptionPool != null ? itemOptionPool.Count : 0;
        int min = Mathf.Max(1, minRewardChoices);
        int max = Mathf.Max(min, maxRewardChoices);

        int desired = Random.Range(min, max + 1);
        desired = Mathf.Clamp(desired, 1, Mathf.Max(1, poolCount));

        List<ItemOptionSO> result = new List<ItemOptionSO>(desired);

        if (poolCount <= 0)
            return result;

        if (allowDuplicateChoices)
        {
            for (int i = 0; i < desired; i++)
                result.Add(itemOptionPool[Random.Range(0, poolCount)]);
            return result;
        }

        // No duplicates: partial Fisher-Yates
        List<ItemOptionSO> temp = new List<ItemOptionSO>(itemOptionPool);
        for (int i = 0; i < desired; i++)
        {
            int swapIndex = Random.Range(i, temp.Count);
            (temp[i], temp[swapIndex]) = (temp[swapIndex], temp[i]);
            result.Add(temp[i]);
        }

        return result;
    }
}
