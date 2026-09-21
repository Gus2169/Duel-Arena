using Unity.Netcode;
using UnityEngine;

public class TestSpin : MonoBehaviour
{
    private void Update()
    {
        // NetworkTransform ne synchronise que ce que le serveur (ou le propriétaire) fait bouger.
        // On limite volontairement le mouvement au serveur pour ce test : c'est exactement le
        // principe "serveur autoritaire" qu'on veut pour PlayerLocomotion plus tard.
        if (!NetworkManager.Singleton.IsServer) return;

        transform.Rotate(Vector3.up, 90f * Time.deltaTime);
    }
}