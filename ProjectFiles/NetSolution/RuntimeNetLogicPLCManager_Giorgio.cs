#region Using directives
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.Retentivity;
using FTOptix.UI;
using FTOptix.NativeUI;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.CommunicationDriver;
using FTOptix.SQLiteStore;
using FTOptix.Store;
#endregion

public class RuntimeNetLogicPLCManager_Giorgio : BaseNetLogic
{
    private List<string> discoveredPlcs = new List<string>();
    private List<string> plcReports = new List<string>();
    private const int pingTimeout = 500; // Timeout per ping in millisecondi

    private UAVariable deviceInfos;
    private UAVariable ipValue;
    [ExportMethod]
    public override void Start()
    {
        Log.Info("PLCManager", "Starting PLC Manager...");
    }
    [ExportMethod]
    public override void Stop()
    {
        Log.Info("PLCManager", "Stopping PLC Manager...");
    }
    [ExportMethod]
    public void StartScanner()
    {
        // prendi il valore della variabile deviceInfos situata in Screens/ScreenGiorgio/TextBox1/ipValue usando il remote read
        ipValue = Project.Current.Get<UAVariable>("UI/Screens/ScreenGiorgio/TextBox1/ipValue");
        deviceInfos = Project.Current.Get<UAVariable>("UI/Screens/ScreenGiorgio/Label1/deviceInfos");

        ipValue.RemoteRead().ToString();

        Log.Info("PLCManager", $"Scanning for PLCs on network...");
        PingSingle();
        DiscoverPlcs();
        TestPlcCommunication();
        LogReports();
    }
    // ping per un solo indirizzo ipValue 
    private void PingSingle()
    {
        var ip = ipValue.RemoteRead();
        Log.Info("PLCManager", $"Pinging {ip}...");
        var plcComm = new PLCCommunication(ip, pingTimeout);
        if (plcComm.Ping())
        {
            Log.Info("PLCManager", $"PLC found at {ipValue.RemoteRead().ToString()}");
            discoveredPlcs.Add(ipValue.RemoteRead().ToString());
        }
    }
    private void DiscoverPlcs()
    {
        for (int i = 1; i < 255; i++)
        {
            string ip = $"192.168.1.{i}";
            var plcComm = new PLCCommunication(ip, pingTimeout);
            if (plcComm.Ping())
            {
                Log.Info("PLCManager", $"PLC found at {ip}, info: {plcComm.GetDeviceInfos()}");
                discoveredPlcs.Add(ip);
            }
        }
    }

    private void TestPlcCommunication()
    {
        foreach (var plc in discoveredPlcs)
        {
            var plcComm = new PLCCommunication(plc); // Crea un'istanza per la comunicazione
            if (plcComm.Ping())
            {
                plcReports.Add($"PLC at {plc} is reachable.");
                deviceInfos.RemoteWrite(plcComm.GetDeviceInfos());
                Log.Info("TestPlcCommunication: ", plcComm.GetDeviceInfos());
                // Leggi tutti i tag disponibili da tutti i driver
                /*
                var tagsByDriver = plcComm.ReadAllTagsFromAvailableDrivers();
                foreach (var driver in tagsByDriver)
                {
                    plcReports.Add($"Driver {driver.Key} found {driver.Value.Count} tags for PLC at {plc}.");
                    foreach (var tag in driver.Value)
                    {
                        // Esegui una lettura del valore per ogni tag
                        var value = tag.RemoteRead();
                        plcReports.Add($"Tag {tag.BrowseName}: Value = {value.Value}");
                    }
                }
                */
            }
            else
            {
                plcReports.Add($"PLC at {plc} is not reachable.");
            }
        }
    }

    private void LogReports()
    {
        foreach (var report in plcReports)
        {
            Log.Info("PLCManager", report);
        }
    }
    private class PLCCommunication
    {
        // enum list of available drivers

        private string ipAddress;
        private int pingTimeout;
        private string [] drivers = { "ModbusTCP", "ModbusRTU", "OPCUA" };
        private string deviceHostName;
        private string deviceAliases;
        private string deviceAddresses;
        private string deviceFullMessage;
        public PLCCommunication(string ipAddress, int timeout = 500)
        {
            this.ipAddress = ipAddress;
            this.pingTimeout = timeout;
        }

        // pinga il dispositivo per verificare la connessione
        public bool Ping()
        {
            using (var ping = new Ping())
            {
                try
                {
                    PingReply reply = ping.Send(ipAddress, pingTimeout);

                    return reply.Status == IPStatus.Success;
                }
                catch (Exception ex)
                {
                    Log.Error($"Error pinging {ipAddress}: {ex.Message}");
                    return false;
                }
            }
        }

        // Ottieni informazioni sul dispositivo pingato
        public string GetDeviceInfos()
        {
            if (Ping())
            {
                try
                {
                    IPHostEntry host = Dns.GetHostEntry(ipAddress);
                    deviceHostName = host.HostName;
                    deviceAliases = string.Join(", ", host.Aliases);
                    deviceAddresses = string.Join(", ", host.AddressList.Select(a => a.ToString()));
                    deviceFullMessage = $"Device at {ipAddress} is reachable. Hostname: {deviceHostName}, Aliases: {deviceAliases}, Addresses: {deviceAddresses}";
                    return deviceFullMessage;
                }
                catch (Exception ex)
                {
                    return $"Error getting device infos for {ipAddress}: {ex.Message}";
                }
            }
            else
            {
                return $"Device at {ipAddress} is not reachable.";
            }
        }
        /*
        public Dictionary<string, List<Tag>> ReadAllTagsFromAvailableDrivers()
        {
            var tagsByDriver = new Dictionary<string, List<Tag>>();

            // Ottieni tutti i driver di comunicazione disponibili
            var drivers = Project.Current.Get<CommunicationDriver>();

            foreach (var driver in drivers)
            {
                try
                {
                    // Prova a connetterti alla stazione per l'IP corrente
                    var station = driver.Get<Station>($"CommDrivers/{driver.Name}/{ipAddress}");

                    if (station != null)
                    {
                        // Esegui il browsing dei tag
                        Struct[] instances;
                        Struct[] prototypes;
                        station.Browse(out instances, out prototypes);

                        var tags = new List<Tag>();
                        foreach (var item in instances)
                        {
                            // Assicurati di aggiungere solo i tag
                            if (item is Tag tagItem)
                            {
                                tags.Add(tagItem); // Aggiungi il tag all'elenco
                            }
                        }

                        // Aggiungi i tag letti dal driver all'elenco
                        if (tags.Count > 0)
                        {
                            tagsByDriver.Add(driver.Name, tags);
                            Log.Info($"Read {tags.Count} tags from {driver.Name} at {ipAddress}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error reading tags from driver {driver.Name} at {ipAddress}: {ex.Message}");
                }
            }

            return tagsByDriver; // Restituisci i tag letti per driver
        }
        */
    }

}

