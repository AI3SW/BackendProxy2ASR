# BackendProxy2ASR

**Agenda**
1)	[Start ASR Engine Docker](#Start-ASR-Engine-Docker)
2)	[Configuration Setting](#Configuration-Setting)
3)	[Start Backend Proxy Service](#Start-Backend-Proxy-Service)
4)	[Authentication](#Authentication)
5)	[Client Simulator](#Client-Simulator)
6)	[ASR Engine Simulator](#ASR-Engine-Simulator)
7)	[Reporting](#Reporting)
8)  [Database](#Database)


##	Start ASR Engine Docker

* Download zip file for ASR Engine from given link. If the file is saved as `*.tgz`, unzip it. Make sure the engine file is end with `*.tar` extension
* Docker service is required to load docker file. 
  * For Windows: [install docker desktop](https://docs.docker.com/docker-for-windows/install/)
  * For Linux: [install docker](https://docs.docker.com/engine/install/)
* Go to docker tar file directory. Run
```bash
$ docker load --input [asrfile].tar
```
* Check if image is loaded
```bash
$ docker images

REPOSITORY          TAG                 IMAGE ID            CREATED             SIZE
[asrfile]           X.X			                    1 second ago         XX GB
```
* Start Docker container 
    * [Linux](https://docs.docker.com/engine/reference/commandline/start/) 
    * [Windows](https://docs.docker.com/docker-for-windows/)

##	Configuration Setting

Configuration file `config.json` contains below items:

* **"Proxy"**: 
  * "proxyPort": Port for Proxy service
  *  "asrIP": ASR Engine IP address e.g.'localhost'
  *  "asrPort": ASR Engine Port
  *  "samplerate": ASR Engine samplerate
  *  "maxConnection": Maximum number of websocket to ASR engine in connection pool

*  **"DummyServer"**:
   * "usingDummy": Whether use dummy ASR Engine
   * "dummyAsrIP": IP address for dummy ASR Engine
   * "dummyAsrPort": Port for dummy ASR Engine
   * "maxConnection": Maximum number of websocket to dummy ASR engine in connection pool
  
*  **"Database"**: 
   * "ToConnect": Whether need to send data to database
   * "Host": IP address for database
   * "Username": Username for database
   * "Password": Password for database user
   * "Database": Database name

*  **"Auth"**:
   * "ToAuthenticate": Whether enable authentication check
   * "AuthMethod": Type of client authentication: `database` or `auth0`
   * "Auth0Domain"
   * "Audience"

*  **"Serilog"**: Logging config

## Authentication

Two types of client authentication are available: `database` and `auth0`. To set the type of authentication, update the `Auth`.`AuthMethod` field in the config file.

### Using Database

Authentication using the `database` method uses a simple username and password system supported by the [pgcrypto](https://www.postgresql.org/docs/9.4/pgcrypto.html) extension in PostgreSQL.

To generate a new password:

```sql
-- Calculates a crypt(3)-style hash of password that is stored in the db
crypt('<password>', gen_salt('bf'))
```

To check a password:

```sql
SELECT user_id
FROM
    "user"
WHERE
    "user".username = username
    AND "user"."password" = crypt('<password>', "user"."password"));
```

Username and password is to be Base64 encoded and included in the `Authorization` field of the websocket connection HTTP header in the following format:

```
Basic <username>:<password>
```

An example is available in [BackendTest.cs](BackEndProxy.Tests/BackendTest.cs) under the `DatabaseTestWithCorrectCredentials` test:

```cs
Dictionary<string, string> headerOptions = new Dictionary<string, string>();
var plainTextBytes = System.Text.Encoding.UTF8.GetBytes("username:password");
headerOptions["Authorization"] = $"Basic {System.Convert.ToBase64String(plainTextBytes)}";
```

### Using Auth0

Client authentication using [Auth0](https://auth0.com/) is also supported. A access token will have to obtained by the client that is included in the `Authorization` field of the websocket connection HTTP header in the following format:

```
Bearer <accessToken>
```

An example is available in [BackendTest.cs](BackEndProxy.Tests/BackendTest.cs) under the `Auth0TestWithCorrectCredentials` test:

```cs
// get access token from Auth0 authentication server
HttpClient client = new HttpClient();
var stringContent = new StringContent(content, Encoding.UTF8, "application/json");

HttpResponseMessage authResponse = await client.PostAsync(authURL, stringContent);
authResponse.EnsureSuccessStatusCode();
string responseBody = await authResponse.Content.ReadAsStringAsync();
string accessToken = (string)JObject.Parse(responseBody)["access_token"];

// test proxy server
headerOptions["Authorization"] = $"Bearer {accessToken}";
```

For client authentication using Auth0, `Auth`.`Auth0Domain` and `Auth`.`Audience` fields are to be included in the config.

##	Start Backend Proxy Service
### Windows
* Start Docker ASR contianer
* Change relavent session in `config.json` and put `config.json` in same directory with `BackendProxy2ASR.exe`
* Double click `BackendProxy2ASR.exe` to start proxy service

### Linux
* Start Docker ASR contianer
* Install **.NET Core 3.1** 
* Change relavent session in `config.json` and put `config.json` in same directory with `BackendProxy2ASR.zip`
* Put `deploy.py` in same directory with `BackendProxy2ASR.zip`
* Run below command
```bash
python deploy.py BackendProxy2ASR.zip config.json
```

##	Client Simulator
A light-weight windows-based client simluator can be found [here](https://github.com/myuan01/DemoFEWs).
 * Open `DemoFEWs.sln` with Visual Studio
 * Install required packages
 * Change `url` in `Main` to correct Backend Proxy address and port
 * Build solution and run

## Dummy ASR Engine

A dummy ASR Engine implemented in Python can be found at this [repository](https://github.com/AI3SW/dummy_asr).

## Database

A PostgreSQL database for this application is setup using docker in another code repository [here](https://github.com/AI3SW/ai_toolbox_db).
