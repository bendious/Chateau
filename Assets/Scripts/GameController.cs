using Platformer.Core;
using UnityEngine;


public class GameController : MonoBehaviour
{
	void Update()
	{
		Simulation.Tick();
	}
}
