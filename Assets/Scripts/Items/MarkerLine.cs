using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;


[RequireComponent(typeof(LineRenderer))]
public class MarkerLine : MonoBehaviour
{
	[SerializeField]
	private float m_distanceMin = 0.2f;
	[SerializeField]
	private float m_throwDrawTimeMax = 0.1f;


	private LineRenderer m_line;


	private void Start()
	{
		m_line = GetComponent<LineRenderer>();

		Color color = transform.parent.GetComponent<SpriteRenderer>().color;
		m_line.startColor = color;
		m_line.endColor = color;

		StartCoroutine(Draw());
	}


	private IEnumerator Draw()
	{
		AddPosition((Vector2)transform.position + new Vector2(0.05f, 0.05f)); // NOTE that we start w/ two slightly-offset positions to make "dots" drawable

		float autoStopTime = float.MaxValue;
		WaitUntil waitCondition = new(() => Vector2.Distance(m_line.GetPosition(m_line.positionCount - 1), transform.position) >= m_distanceMin);

		while (transform.parent != null && autoStopTime > Time.time)
		{
			AddPosition(transform.position);

			if (transform.parent.parent == null && autoStopTime == float.MaxValue)
			{
				autoStopTime = Time.time + m_throwDrawTimeMax;
			}

			yield return waitCondition;
		}

		transform.SetParent(null);
		SceneManager.MoveGameObjectToScene(gameObject, SceneManager.GetActiveScene()); // "cancel" the effect of DontDestroyOnLoad() since we were probably attached to an avatar; see https://answers.unity.com/questions/1491238/undo-dontdestroyonload.html
	}

	private void AddPosition(Vector2 pos)
	{
		m_line.SetPosition(m_line.positionCount++, pos);
	}
}
