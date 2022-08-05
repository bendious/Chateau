using System.Collections;
using System.Linq;
using UnityEngine;


public class BackgroundResize : MonoBehaviour
{
	[SerializeField] private Vector3 m_scalar = new(1.5f, 1.5f, 1.0f);


	private void Start()
	{
		IEnumerator delayedSetTransform()
		{
			yield return new WaitForEndOfFrame(); // due to GameController.RoomBackdropsAboveGround needing to access m_startRoom
			Bounds lookoutBbox = GameController.Instance.RoomBackdropsAboveGround.Aggregate(new Bounds(), (bbox, room) => // TODO: don't assume the origin should be contained?
			{
				bbox.Encapsulate(room.GetComponent<SpriteRenderer>().bounds);
				return bbox;
			});
			transform.localScale = Vector3.Scale(lookoutBbox.extents, m_scalar);
			transform.position = new(lookoutBbox.center.x, lookoutBbox.min.y, lookoutBbox.center.z);
		}
		StartCoroutine(delayedSetTransform());
	}
}
