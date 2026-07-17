using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class UdpReceiver : MonoBehaviour
{
    public int port = 8000;
    private UdpClient udpClient;
    private Thread receiveThread;
    private DroneController drone;

    private float cmdY, cmdZ, cmdRotate;
    private string cmdAction = "hover";
    private bool newCommand = false;

    void Start()
    {
        drone = GetComponent<DroneController>();
        udpClient = new UdpClient(port);
        receiveThread = new Thread(ReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();
        Debug.Log("UDP listening on port " + port);
    }

    void ReceiveLoop()
    {
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            try
            {
                byte[] data = udpClient.Receive(ref endPoint);
                string json = Encoding.UTF8.GetString(data);
                ParseCommand(json);
            }
            catch { }
        }
    }

    void ParseCommand(string json)
    {
        json = json.Replace("{", "").Replace("}", "").Replace("\"", "");
        foreach (var part in json.Split(','))
        {
            var kv = part.Split(':');
            if (kv.Length != 2) continue;
            string key = kv[0].Trim();
            string val = kv[1].Trim();

            if (key == "y") float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cmdY);
            if (key == "z") float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cmdZ);
            if (key == "rotate") float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cmdRotate);
            if (key == "action") cmdAction = val;
        }
        newCommand = true;
    }

    void Update()
    {
        if (!newCommand) return;
        newCommand = false;

        switch (cmdAction)
        {
            case "toggle":
                if (drone.isFlying)
                    drone.Land();
                else
                    drone.TakeOff();
                break;
            case "fly":
                drone.MoveFromHand(cmdY, cmdZ, cmdRotate);
                break;
            case "hover":
                break;
        }
    }

    void OnDestroy()
    {
        receiveThread?.Abort();
        udpClient?.Close();
    }
}
