using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class KeyGrabController : MonoBehaviour
{
    [Header("Input")]
    public KeyCode grabKey = KeyCode.G;

    [Header("Filtering")]
    public string grabbableTag = "Grabbable";
    public float maxGrabDistance = 0.25f; // safety filter

    [Header("Attachment")]
    public Transform attachPoint;

    private readonly List<Collider> _cands = new();
    private GameObject _held;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true; // must be trigger
    }

    void Awake()
    {
        if (attachPoint == null) attachPoint = transform;
    }

    void Update()
    {
        if (Input.GetKeyDown(grabKey))
        {
            if (_held == null)
            {
                var c = FindBestCandidate();
                if (c != null) Grab(c);
            }
        }
        else if (Input.GetKeyUp(grabKey))
        {
            if (_held != null) Drop();
        }
    }

    GameObject FindBestCandidate()
    {
        if (_cands.Count == 0) return null;
        Transform t = attachPoint != null ? attachPoint : transform;
        float best = float.PositiveInfinity;
        GameObject bestGO = null;

        foreach (var c in _cands)
        {
            if (c == null) continue;
            var go = c.gameObject;
            if (!go.CompareTag(grabbableTag)) continue;

            float d = Vector3.Distance(c.ClosestPoint(t.position), t.position);
            if (d <= maxGrabDistance && d < best) { best = d; bestGO = go; }
        }
        return bestGO;
    }

    void Grab(GameObject obj)
    {
        _held = obj;
        var rb = _held.GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = true; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }

        _held.transform.SetParent(attachPoint, true);
        _held.transform.localPosition = Vector3.zero;
        _held.transform.localRotation = Quaternion.identity;
    }

    void Drop()
    {
        var rb = _held.GetComponent<Rigidbody>();
        _held.transform.SetParent(null, true);
        if (rb) rb.isKinematic = false;
        _held = null;
    }

    void OnTriggerEnter(Collider other){ Debug.Log("HAND trigger enter: " + other.name); if(!_cands.Contains(other)) _cands.Add(other); }
    void OnTriggerExit(Collider other){ Debug.Log("HAND trigger exit: " + other.name); _cands.Remove(other); }
}
