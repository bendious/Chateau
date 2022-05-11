using System.Collections;
using UnityEngine;


public class PlatformFlipper : MonoBehaviour
{
	[SerializeField]
	private float m_updateSeconds = 1.0f;


	private PlatformEffector2D m_platform;


	private void OnEnable()
	{
		m_platform = GetComponent<PlatformEffector2D>();
		StartCoroutine(ManageUpCoroutine());
	}


	private IEnumerator ManageUpCoroutine()
	{
		yield return new WaitForSeconds(Random.Range(0.0f, m_updateSeconds)); // to spread out updates of separate PlatformFlippers

		WaitForSeconds wait = new(m_updateSeconds);
		while (true)
		{
			m_platform.rotationalOffset = (transform.rotation * Vector2.up).y < 0.0f ? 180.0f : 0.0f;
			yield return wait;
		}
	}
}
