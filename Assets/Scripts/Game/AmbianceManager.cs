using System.Collections;
using UnityEngine;


public class AmbianceManager : MonoBehaviour
{
	[SerializeField] private AudioSource m_source;

	[SerializeField] private float m_smoothTime = 0.5f;

	[SerializeField] private AnimationCurve m_volumeCurve = AnimationCurve.EaseInOut(0.0f, 0.0f, 1.0f, 1.0f);


	private float m_curvePct = 0.0f;
	private float m_curveVelocity = 0.0f;


	private void OnEnable() => StartCoroutine(VolumeCoroutine());

	private void OnDisable() => StopAllCoroutines();


	private IEnumerator VolumeCoroutine()
	{
		yield return new WaitUntil(() => !GameController.IsSceneLoad); // to prevent volume spike from null bbox size while loading

		while (true)
		{
			yield return null; // TODO: efficiency?
			float curvePctTarget = Camera.main.transform.position.y / Mathf.Max(Utility.FloatEpsilon, GameController.Instance.m_ctGroupOverview.BoundingBox.size.y); // TODO: base on amount of building above the current camera position? take nearby windows into account?
			m_curvePct = Mathf.SmoothDamp(m_curvePct, curvePctTarget, ref m_curveVelocity, m_smoothTime);
			m_source.volume = m_volumeCurve.Evaluate(m_curvePct);
		}
	}
}
