using UnityEngine;


[DisallowMultipleComponent]
public class TimedDespawn : MonoBehaviour
{
	public float m_lifetime = 3.0f;


	private void Start()
	{
		Simulation.Schedule<ObjectDespawn>(m_lifetime).m_object = gameObject;
	}
}
