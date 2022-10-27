using System.Linq;
using TMPro;
using UnityEngine;


public class LoadingTip : MonoBehaviour
{
	[SerializeField] private WeightedObject<string[]>[] m_tips; // NOTE that we use string[] only due to the Inspector not liking TextArea arrays


	private void OnEnable()
	{
		GetComponent<TMP_Text>().text = m_tips.RandomWeighted().Aggregate("", (output, nextLine) => output + "\n" + nextLine);
	}
}
