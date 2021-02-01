﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Fleck;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

using Serilog;
using database_and_log;


namespace BackendProxy2ASR
{
    class AnswerPlusSessionID
    {
        public string right_text { get; set; }
        public string session_id { get; set; }
        public int sequence_id { get; set; }
    }

    class UserInfo
    {
        public string username { get; set; }
        public string password { get; set; }
    }

    class ProxyASR
    {
        private readonly int m_proxyPort;
        private readonly String m_asrIP;
        private readonly int m_asrPort;
        private readonly int m_sampleRate;

        private List<IWebSocketConnection> m_allSockets;
        private Dictionary<String, IWebSocketConnection> m_sessionID2sock;
        private Dictionary<IWebSocketConnection, String> m_sock2sessionID;

        private CommASR m_commASR = null;
        //private DatabaseHelper dbhelper = null;
        private Dictionary<String, SessionHelper> m_sessionID2Helper;

        private DatabaseHelper databaseHelper = new DatabaseHelper("../config.json");
        private ILogger _logger = new LogHelper<ProxyASR>("../config.json").Logger;
        private UserCredential savedUserCrednetial = new UserCredential();

        //--------------------------------------------------------------------->
        // C'TOR: initialize member variables
        //--------------------------------------------------------------------->
        public ProxyASR(int proxyPort, String asrIP, int asrPort, int sampleRate)
        {
            m_proxyPort = proxyPort;
            m_asrIP = asrIP;
            m_asrPort = asrPort;
            m_sampleRate = sampleRate;
            m_allSockets = new List<IWebSocketConnection>();
            m_sessionID2sock = new Dictionary<String, IWebSocketConnection>();
            m_sock2sessionID = new Dictionary<IWebSocketConnection, String>();
            m_sessionID2Helper = new Dictionary<String, SessionHelper>();

            m_commASR = CommASR.Create(asrIP, asrPort, sampleRate, m_sessionID2sock, m_sessionID2Helper);
        }


        public List<IWebSocketConnection> allSockets
        {
            get { return m_allSockets; }
        }


        public void Start()
        {
            FleckLog.Level = LogLevel.Error;
            var server = new WebSocketServer("ws://0.0.0.0:" + m_proxyPort);
            _logger.Information("Starting Fleck WebSocket Server...");
            _logger.Information("port: " + m_proxyPort + "  samplerate: " + m_sampleRate);

            server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        OnConnect(socket);
                    };

                    socket.OnClose = () =>
                    {
                        OnDisconnect(socket);
                    };

                    socket.OnMessage = message =>
                    {
                        OnMessage(socket, message);
                    };

                    socket.OnBinary = data =>
                    {
                        OnBinaryData(socket, data);
                    };
                }
            );
        }

        //--------------------------------------------------------------------->
        // Handle new websocket connect
        //--------------------------------------------------------------------->
        private void OnConnect(IWebSocketConnection sock)
        {
            _logger.Information("WS Connect...");
            var session = new SessionHelper();
            sock.Send("0{\"session_id\":" + session.m_sessionID + "}");
            m_sessionID2Helper[session.m_sessionID] = session;
            m_allSockets.Add(sock);
        }

        //--------------------------------------------------------------------->
        // Handle websocket disconnect
        //--------------------------------------------------------------------->
        private void OnDisconnect(IWebSocketConnection sock)
        {
            _logger.Information("WS Disconnect...");
            // Disconnect from ASR engine
            var session_id = m_sock2sessionID[sock];
            if (String.IsNullOrEmpty(session_id) == false)
            {
                m_commASR.DisconnectASR(session_id);
            }
            
            m_allSockets.Remove(sock);
        }

        //--------------------------------------------------------------------->
        // Handle websocket text message
        //--------------------------------------------------------------------->
        private void OnMessage(IWebSocketConnection sock, String msg)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _logger.Information(msg);

            sock.Send("Echo: " + msg);

            if (msg.Contains("username") == true || msg.Contains("password") == true)
            {
                Console.WriteLine("Receive user information...");
                UserInfo user = JsonConvert.DeserializeObject<UserInfo>(msg);
                if (CheckUserCredential(user.username, user.password) == false)
                {
                    sock.OnClose();
                    //return;
                }
                return;
            }

            if (msg.Contains("right_text")==false || msg.Contains("session_id")==false || msg.Contains("sequence_id") == false)
            {
                _logger.Error("message missing 'right_text' OR 'session_id' OR 'sequence_id' .... ignored ....");
                return;
            }

            AnswerPlusSessionID aps = JsonConvert.DeserializeObject<AnswerPlusSessionID>(msg);
            _logger.Information(aps.right_text);
            _logger.Information(aps.session_id);
            _logger.Information(aps.sequence_id.ToString());

            if (m_sock2sessionID.ContainsKey(sock)==false)
            {
                //------------------------------------------------------------->
                // it is a new session from client:
                //      (1) create websocket connection to ASR Engine
                //------------------------------------------------------------->
                m_sock2sessionID[sock] = aps.session_id;
                m_sessionID2sock[aps.session_id] = sock;

                _logger.Information("map sock -> sessionID: " + aps.session_id);
                _logger.Information("map sessionID " + aps.session_id + " -> sock");

                m_commASR.ConnectASR(aps.session_id);
            }

            //----------------------------------------------------------------->
            // Store session and sequence information
            //----------------------------------------------------------------->
            try
            {
                var session = m_sessionID2Helper[aps.session_id];
                if (session.m_sequence2inputword.ContainsKey(aps.sequence_id) == false)
                {
                    session.m_sequence2inputword[aps.sequence_id] = aps.right_text;
                    session.m_sequenceQueue.Enqueue(aps.sequence_id);
                    session.m_sequenceStartTime[aps.sequence_id] = DateTime.UtcNow;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Retrieve Session informatino error: " + e.ToString());
            }
            

            //----------------------------------------------------------------->
            // Initialize recrod into database
            //----------------------------------------------------------------->
            bool connectionResult = databaseHelper.Open();
            _logger.Information("Opening connection success? : " + connectionResult.ToString());

            // update asr_audio_stream_prediction
            databaseHelper.InsertAudioStreamPrediction(
                user_id: 1,
                device_id: "device1",
                app_id: 1,
                session_id: aps.session_id,
                seq_id: aps.sequence_id,
                pred_timestamp: DateTime.UtcNow,
                input_word: aps.right_text,
                return_text: "");

            databaseHelper.InsertAudioStreamInfo(
                user_id: 1,
                device_id: "device1",
                app_id: 1,
                session_id: aps.session_id,
                seq_id: aps.sequence_id,
                proc_start_time: DateTime.UtcNow,
                proc_end_time: DateTime.UtcNow,
                stream_duration: 0);
        }

        //--------------------------------------------------------------------->
        // Handle websocket text message
        //--------------------------------------------------------------------->
        private void OnBinaryData(IWebSocketConnection sock, byte[] data)
        {
            var sessionID = m_sock2sessionID[sock];
            m_commASR.SendBinaryData(sessionID, data);

            var session = m_sessionID2Helper[sessionID];
            var sequenceID = session.GetCurrentSequenceID();
            session.StoreIncommingBytes(sequenceID, data);
        }

        //--------------------------------------------------------------------->
        // Check user credential
        //--------------------------------------------------------------------->
        private bool CheckUserCredential (string username, string password)
        {
            var savedCredential = savedUserCrednetial.Credential;
            if (savedCredential.ContainsKey(username) == false)
            {
                Console.WriteLine("Invalid username. Disconnect...");
                return false;
            } else if (savedCredential[username] != password)
            {
                Console.WriteLine("Incorrect password for " + username+ ". Disconnect...");
                return false;
            }
            return true;
        }
    }

}
