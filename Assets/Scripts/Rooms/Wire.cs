using System.Linq;
using UnityEngine;


public class Wire : MonoBehaviour
{
	[SerializeField] private float m_incrementDegrees = 45.0f;
	[SerializeField] private float m_unactiveColorPct = 0.5f;


	private void Start()
	{
		IKey key = GetComponent<IKey>();
		LineRenderer line = GetComponent<LineRenderer>();
		const RoomController.PathFlags pathFlags = RoomController.PathFlags.IgnoreGravity | RoomController.PathFlags.NearestEndPoints;
		System.Collections.Generic.List<Vector2> path = GameController.Instance.RoomFromPosition(transform.position).PositionPath(gameObject, key != null ? key.Lock.Component.gameObject : GetComponent<IUnlockable>().Parent, pathFlags, Mathf.Max(line.startWidth, line.endWidth), incrementDegrees: m_incrementDegrees).Item1;

		line.positionCount = path.Count;
		line.SetPositions(path.Select(pos2D => (Vector3)pos2D).ToArray());
		line.colorGradient = new() { colorKeys = new GradientColorKey[] { new(GetComponent<SpriteRenderer>().color * m_unactiveColorPct, 0.0f) }, alphaKeys = new GradientAlphaKey[] { new(1.0f, 0.0f) } };
	}
}
