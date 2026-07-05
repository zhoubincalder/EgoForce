using UnityEngine;
using TMPro;

public class IndexDistanceHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI distanceText;

    [Header("Formatting")]
    public int decimals = 3;
    public string prefix = "Index distance: ";
    private void Awake()
    {
        // auto-grab if not assigned in Inspector
        if (!distanceText)
            distanceText = GetComponentInChildren<TextMeshProUGUI>(true); // true = include inactive
    }

    // Call this from wherever you're computing the distance
    public void SetDistance(float d)
    {
        Debug.Log($"IndexDistanceHUD: SetDistance({d})");   
        if (distanceText == null) return;
        distanceText.text = $"{prefix}{d.ToString($"F{decimals}")}";
    }
}
