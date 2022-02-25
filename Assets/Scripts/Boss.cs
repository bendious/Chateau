using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


public class Boss : MonoBehaviour
{
	public BossRoom m_room;


	private Vector3 m_startPos;

	private bool m_started = false;
	private bool m_activatedFully = false;

	private bool m_isQuitting = false;


	private void Awake()
	{
		m_startPos = transform.position;
	}

	private void OnWillRenderObject()
	{
		if (m_started)
		{
			return;
		}

		// ignore until we are well within camera view
		if (GameController.IsReloading || !GameController.Instance.m_avatars.Exists(avatar =>
		{
			if (!avatar.IsAlive)
			{
				return false;
			}
			Vector2 screenPos = avatar.m_camera.WorldToViewportPoint(transform.position);
			const float edgePct = 0.1f; // TODO: parameterize?
			return screenPos.x > edgePct && screenPos.x < 1.0f - edgePct && screenPos.y > edgePct && screenPos.y < 1.0f - edgePct;
		}))
		{
			return;
		}

		m_started = true;
		StopAllCoroutines();
		StartCoroutine(UpdateIntro());
	}

	private void OnBecameInvisible()
	{
		// if we're in the pause before true activation, wait until we're back on camera
		if (m_started && !m_activatedFully)
		{
			StopAllCoroutines();
			m_started = false;
		}
	}

	private void OnDestroy()
	{
		if (m_isQuitting || GameController.IsReloading)
		{
			return;
		}

		// TODO: start zoom-in?

		m_room.EndMusic();
		GameController.Instance.OnVictory();
	}

	private void OnApplicationQuit()
	{
		m_isQuitting = true;
	}


#if DEBUG
	public void DebugReset()
	{
		StopAllCoroutines();
		m_started = false;
		m_activatedFully = false;
		EnemyController controller = GetComponent<EnemyController>();
		controller.enabled = false;
		controller.Teleport(m_startPos); // TODO: set goal and navigate rather than snapping?
		GetComponent<Health>().m_invincible = true;
		foreach (CinemachineVirtualCamera vCam in AllVirtualCameras())
		{
			vCam.m_Lens.OrthographicSize = 3.0f; // TODO: un-hardcode?
		}
	}
#endif


	private IEnumerator UpdateIntro()
	{
		const float smoothTimeSlow = 0.5f;
		const float smoothTimeFast = 0.25f;

		// pause
		yield return new WaitForSeconds(2.0f);

		m_activatedFully = true;

		// recolor
		SpriteRenderer[] bossRenderers = GetComponentsInChildren<SpriteRenderer>().Where(renderer => renderer.GetComponent<ItemController>() == null).ToArray();
		Light2D bossLight = GetComponent<Light2D>();
		const float intensityTarget = 1.0f;
		Vector4 colorSpeedCur = Vector4.zero;
		float lightSpeedCur = 0.0f;
		while (!Utility.ColorsSimilar(bossRenderers.First().color, Color.red) || !Utility.FloatEqual(bossLight.intensity, intensityTarget)) // TODO: don't assume colors are always in sync?
		{
			foreach (SpriteRenderer bossRenderer in bossRenderers)
			{
				bossRenderer.color = Utility.SmoothDamp(bossRenderer.color, Color.red, ref colorSpeedCur, smoothTimeSlow);
			}
			bossLight.intensity = Mathf.SmoothDamp(bossLight.intensity, intensityTarget, ref lightSpeedCur, smoothTimeSlow);
			yield return null;
		}

		// float into air
		float yTarget = transform.position.y + 3.0f;
		float ySpeedCur = 0.0f;
		while (!Utility.FloatEqual(transform.position.y, yTarget))
		{
			Vector3 pos = transform.position;
			pos.y = Mathf.SmoothDamp(transform.position.y, yTarget, ref ySpeedCur, smoothTimeSlow);
			transform.position = pos;
			yield return null;
		}

		// reveal arms
		ArmController[] arms = GetComponentsInChildren<ArmController>();
		float degrees = 0.0f;
		float angleVel = 0.0f;
		while (degrees < 359.0f)
		{
			Vector2 aimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, degrees) * Vector2.right;
			for (int i = (int)degrees * arms.Length / 360; i < arms.Length; ++i)
			{
				arms[i].UpdateAim(GetComponent<KinematicCharacter>().m_armOffset, aimPos, aimPos);
			}
			degrees = Mathf.SmoothDamp(degrees, 360.0f, ref angleVel, smoothTimeSlow);
			yield return null;
		}

		m_room.AmbianceToMusic();

		// zoom camera(s) out
		IEnumerable<CinemachineVirtualCamera> vCams = AllVirtualCameras();
		float zoomSpeedCur = 0.0f; // TODO: per-cam?
		float zoomSizeTarget = 5.0f;
		while (!Utility.FloatEqual(vCams.First().m_Lens.OrthographicSize, zoomSizeTarget)) // TODO: don't assume cameras are in lockstep?
		{
			foreach (CinemachineVirtualCamera vCam in vCams)
			{
				vCam.m_Lens.OrthographicSize = Mathf.SmoothDamp(vCam.m_Lens.OrthographicSize, zoomSizeTarget, ref zoomSpeedCur, smoothTimeFast);
			}
			yield return null;
		}

		// enable boss
		GetComponent<Health>().m_invincible = false;
		GetComponent<EnemyController>().enabled = true;
	}

	private IEnumerable<CinemachineVirtualCamera> AllVirtualCameras()
	{
		return Camera.allCameras.Select(camera => camera.GetComponent<CinemachineBrain>().ActiveVirtualCamera.VirtualCameraGameObject.GetComponent<CinemachineVirtualCamera>());
	}
}
