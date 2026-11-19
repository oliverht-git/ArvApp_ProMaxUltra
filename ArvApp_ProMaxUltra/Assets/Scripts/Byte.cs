using UnityEngine;

public class Byte : Arv
{
    public GameObject startskärm;
    public GameObject spelskärm;
    public GameObject MC;

    public void Start()
    {
        MC.transform.rotation = Quaternion.Euler(0, -125, 0);
        startskärm.SetActive(true);
        spelskärm.SetActive(false);
        for (int i = 0; i < fordon.Length; i++)
        {
            fordon[i].SetActive(false);
            fordon[index].SetActive(true);
        }
    }
    public void nasta()
    {
        if (index < fordon.Length - 1)
        {
            index += 1;
        }
        else
        {
            index = 0;
        }

        for (int i = 0; i < fordon.Length; i++)
        {
            fordon[i].SetActive(false);
        }
        fordon[index].SetActive(true);
    }

    public void foregaende()
    {
        if (index > 0)
        {
            index -= 1;
        }
        else
        {
            index = fordon.Length - 1;
        }

        for (int i = 0; i < fordon.Length; i++)
        {
            fordon[i].SetActive(false);
        }
        fordon[index].SetActive(true);
    }

    public void start()
    {
        startskärm.SetActive(false);
        spelskärm.SetActive(true);
        MC.transform.rotation = Quaternion.Euler(0, 0, 0);
    }
}
