using UnityEngine;
using UnityEngine.VFX;


[DisallowMultipleComponent]
public class TimedDespawn : MonoBehaviour
{
	public float m_lifetime = 3.0f;


	private void Start()
	{
		StartCoroutine(DespawnDelayedViaSoftStop());
	}


	private System.Collections.IEnumerator DespawnDelayedViaSoftStop()
	{
		yield return new WaitForSeconds(m_lifetime);

		VisualEffect vfx = GetComponent<VisualEffect>();
		if (vfx != null)
		{
			yield return StartCoroutine(vfx.SoftStop(wholeObject: false)); // NOTE the synchronous coroutine nesting; see https://www.alanzucconi.com/2017/02/15/nested-coroutines-in-unity/ // NOTE also that we don't want to deactivate the whole object afterward to avoid preempting the rest of this coroutine
		}

		Simulation.Schedule<ObjectDespawn>().m_object = gameObject;
	}
}
