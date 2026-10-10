using UnityEngine;

public class WeaponTester : MonoBehaviour
{
    public RemoteConfigLoader loader;

    void Start()
    {
        loader.LoadRoutine(weapons =>
        {
            foreach (var w in weapons)
                Debug.Log(" -> " + w);
        });
    }
}