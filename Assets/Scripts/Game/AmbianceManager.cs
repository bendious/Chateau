using System.Collections;
using UnityEngine;


public class AmbianceManager : MonoBehaviour
{
	[SerializeField] private AudioSource m_source;

	[SerializeField] private float m_smoothTime = 0.5f;


	private float m_volumeVelocity = 0.0f;


	private void OnEnable() => StartCoroutine(VolumeCoroutine());

	private void OnDisable() => StopAllCoroutines();


	private IEnumerator VolumeCoroutine()
	{
		while (true)
		{
			yield return null; // TODO: efficiency?
			float volumeTarget = Mathf.Clamp01(Camera.main.transform.position.y / GameController.Instance.m_ctGroupOverview.BoundingBox.size.y); // TODO: base on amount of building above the current camera position? take nearby windows into account?
			m_source.volume = Mathf.SmoothDamp(m_source.volume, volumeTarget, ref m_volumeVelocity, m_smoothTime);
		}
	}
}
