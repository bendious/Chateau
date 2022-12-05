using System.Collections;
using UnityEngine;


public class BackgroundResize : MonoBehaviour
{
	[SerializeField] private Vector3 m_margin = new(30.0f, 15.0f, 0.0f); // TODO: determine automatically based on camera/avatar settings?


	private void Start()
	{
		IEnumerator delayedSetTransform()
		{
			yield return new WaitUntil(() => !GameController.IsSceneLoad); // due to needing GameController.m_ctGroupOverview.BoundingBox and GameController.LoadCoroutine() taking an indeterminate number of frames
			Bounds lookoutBbox = GameController.Instance.m_ctGroupOverview.BoundingBox;
			Vector3 extentsExpanded = lookoutBbox.extents + m_margin;
			float aspectRatio = Camera.main.aspect;
			Vector3 aspectRatioMins = new(extentsExpanded.y * aspectRatio, extentsExpanded.x / aspectRatio, 0.0f); // since very wide/short and very tall/narrow layouts cause the camera to zoom significantly farther than the extents in one direction
			transform.localScale = Vector3.Max(extentsExpanded, aspectRatioMins);
			transform.position = new(lookoutBbox.center.x, transform.position.y, lookoutBbox.center.z);
		}
		StartCoroutine(delayedSetTransform());
	}
}
