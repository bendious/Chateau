using System.Linq;
using UnityEngine;


public class EntrywayLock : MonoBehaviour
{
	[SerializeField] private int[] m_unlockSceneIndices; // TODO: determine automatically?


	private void Start()
	{
		if (m_unlockSceneIndices.Contains(GameController.SceneIndexPrev))
		{
			GetComponent<LockController>().Unlock(null, true);
		}
	}
}
