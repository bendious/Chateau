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
		System.Collections.Generic.List<Vector2> path = GameController.Instance.RoomFromPosition(transform.position).PositionPath(transform.position, (key != null ? key.Lock.Component.gameObject : GetComponent<IUnlockable>().Parent).transform.position, Vector2.zero, RoomController.ObstructionCheck.None, Mathf.Max(line.startWidth, line.endWidth), float.MaxValue, m_incrementDegrees);

		line.positionCount = path.Count;
		line.SetPositions(path.Select(pos2D => (Vector3)pos2D).ToArray());
		line.colorGradient = new Gradient() { colorKeys = new GradientColorKey[] { new GradientColorKey(GetComponent<SpriteRenderer>().color * m_unactiveColorPct, 0.0f) }, alphaKeys = new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f) } };
	}
}
