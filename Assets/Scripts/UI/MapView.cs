using System.Collections;
using UnityEngine;


public class MapView : MonoBehaviour
{
	private void Start()
	{
		PlatformEffector2D effector = GetComponentInParent<PlatformEffector2D>();
		if (effector != null)
		{
			StartCoroutine(UpdateRenderer(effector));
		}
	}

	private IEnumerator UpdateRenderer(PlatformEffector2D effector)
	{
		SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
		bool effectorEnabled = true;
		WaitUntil wait = new WaitUntil(() => effector.enabled != effectorEnabled);

		while (effector != null)
		{
			effectorEnabled = effector.enabled;
			spriteRenderer.color = effectorEnabled ? Color.gray : Color.white;
			yield return wait;
		}
	}
}
