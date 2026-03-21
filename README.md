# Kafka Consumer & Producer Example (C#)

.NET 10 Kafka producer app. Supports mutual TLS (mTLS) and Azure Entra ID (OAuthBearer) authentication.

## Project Structure

- `consumer/` - Kafka consumer that reads messages from a topic
- `producer/` - Kafka producer that publishes the current UTC time every second

## Prerequisites

- .NET 10 SDK
- `kubectl` access to the cluster (for local development with mTLS)
- Azure CLI (for local development with OAuthBearer)

## Configuration

### appsettings.json

The producer and consumer each have their own `appsettings.json`. The producer also supports [.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for local development.

**Consumer `appsettings.json`:**

```json
{
  "Kafka": {
    "BootstrapServers": "your-kafka-broker:9094",
    "GroupId": "your-consumer-group",
    "Topic": "your-topic",
    "AutoOffsetReset": "Earliest",
    "EnableAutoCommit": true,
    "Security": {
      "SecurityProtocol": "SaslSsl",
      "SaslMechanism": "OAuthBearer",
      "SaslOauthbearerClientId": "<your-app-registration-client-id>",
      "SaslOauthbearerScope": "<your-app-registration-client-id>/.default"
    },
    "Ssl": {
      "SslEndpointIdentificationAlgorithm": "Https",
      "SslCaLocation": "ca-kafka.pem",
      "EnableInsecureSsl": "false"
    }
  }
}
```

**Producer `appsettings.json`:**

```json
{
  "Kafka": {
    "BootstrapServers": "your-kafka-broker:9094",
    "Topic": "your-topic",
    "Key": "",
    "Security": {
      "SecurityProtocol": "SaslSsl",
      "SaslMechanism": "OAuthBearer"
    },
    "Ssl": {
      "SslCaLocation": "ca-kafka.pem",
      "EnableInsecureSsl": "false"
    }
  }
}
```

**Producer `appsettings.json` (mTLS):**

For mutual TLS, set `SecurityProtocol` to `Ssl` and provide the client certificate and key. The OAuthBearer handler is skipped automatically.

```json
{
  "Kafka": {
    "BootstrapServers": "your-kafka-broker:9093",
    "Topic": "your-topic",
    "Key": "",
    "Security": {
      "SecurityProtocol": "Ssl"
    },
    "Ssl": {
      "SslCaLocation": "ca-kafka.pem",
      "SslCertificateLocation": "client.pem",
      "SslKeyLocation": "client.key",
      "SslKeyPassword": "",
      "EnableInsecureSsl": "false"
    }
  }
}
```

mTLS can also be combined with SASL (`SecurityProtocol: SaslSsl`) — in that case both `SslCertificateLocation`/`SslKeyLocation` and the OAuthBearer handler are active simultaneously.

The `Key` field sets the Kafka message key. Leave it empty to use no key.

## Configuring Azure Entra ID

### Step 1: Create an App Registration

1. Go to the [Azure Portal](https://portal.azure.com)
2. Navigate to **Microsoft Entra ID** > **App registrations**
3. Click **New registration**
4. Enter a name for your application (e.g., `kafka-consumer`)
5. Select the appropriate account type (typically "Accounts in this organizational directory only")
6. Click **Register**
7. Note the **Application (client) ID** - you'll need this for `SaslOauthbearerClientId`

### Step 2: Configure Federated Credentials

Federated credentials allow your application to authenticate without secrets by trusting tokens from an external identity provider (like Kubernetes).

1. In your App Registration, go to **Certificates & secrets**
2. Select the **Federated credentials** tab
3. Click **Add credential**
4. Select the scenario:

#### For Azure Kubernetes Service (AKS):

1. Select **Kubernetes accessing Azure resources**
2. Fill in:
   - **Cluster issuer URL**: Your AKS OIDC issuer URL (find it with `az aks show --name <aks-name> --resource-group <rg> --query "oidcIssuerProfile.issuerUrl" -o tsv`)
   - **Namespace**: The Kubernetes namespace where your app runs
   - **Service account**: The Kubernetes service account name
   - **Name**: A descriptive name for this credential
3. Click **Add**

#### For GitHub Actions:

1. Select **GitHub Actions deploying Azure resources**
2. Fill in:
   - **Organization**: Your GitHub organization or username
   - **Repository**: Your repository name
   - **Entity type**: Branch, Tag, or Environment
   - **GitHub entity name**: The branch/tag/environment name
3. Click **Add**

#### For Other Identity Providers:

1. Select **Other issuer**
2. Fill in:
   - **Issuer**: The OIDC issuer URL
   - **Subject identifier**: The subject claim value
   - **Name**: A descriptive name
3. Click **Add**

### Step 3: Grant Kafka Permissions

Ensure your App Registration has the necessary permissions to access Kafka:

1. Contact your Kafka administrator to add your App Registration's Client ID to the appropriate ACLs
2. The scope in your configuration should be `<client-id>/.default`

## Running the Application

### Local Setup (Producer)

**1. Clone the repo and restore dependencies:**

```bash
git clone <repo-url>
cd kafka-example-csharp/producer
dotnet restore
```

**2. Create `appsettings.Development.json`:**

This file is gitignored. Create it in `producer/` with your local settings:

```json
{
  "HealthPort": "8081",
  "Kafka": {
    "BootstrapServers": "localhost:9093",
    "Topic": "example-topic",
    "Key": "",
    "Security": {
      "SecurityProtocol": "Ssl"
    },
    "Ssl": {
      "SslCaLocation": "ca.crt",
      "SslCertificateLocation": "client.pem",
      "SslKeyLocation": "client.key",
      "EnableInsecureSsl": "true"
    }
  }
}
```

**3. Extract the TLS certificates from the cluster** (mTLS only):

```bash
kubectl get secret csharp-example-client-cert -n kafka -o jsonpath='{.data.tls\.crt}' | base64 -d > client.pem
kubectl get secret csharp-example-client-cert -n kafka -o jsonpath='{.data.tls\.key}' | base64 -d > client.key
kubectl get secret kafka-brokers-cert -n kafka -o jsonpath='{.data.ca\.crt}' | base64 -d > ca.crt
```

These files are gitignored and must be re-extracted if the certs are rotated.

**4. Port-forward the Kafka bootstrap service** (keep running in a separate terminal):

```bash
kubectl port-forward svc/kafka-cluster-kafka-bootstrap -n kafka 9093:9093
```

**5. Run:**

```bash
dotnet run --launch-profile Development
```

### Local Development

The producer loads `appsettings.Development.json` on top of `appsettings.json` when `DOTNET_ENVIRONMENT=Development` is set, making it easy to override settings without touching the base config.

#### mTLS (recommended)

1. **Extract the client certificate and broker CA from the cluster:**

```bash
kubectl get secret csharp-example-client-cert -n kafka -o jsonpath='{.data.tls\.crt}' | base64 -d > producer/client.pem
kubectl get secret csharp-example-client-cert -n kafka -o jsonpath='{.data.tls\.key}' | base64 -d > producer/client.key
kubectl get secret kafka-brokers-cert -n kafka -o jsonpath='{.data.ca\.crt}' | base64 -d > producer/ca.crt
```

2. **Port-forward the Kafka bootstrap service** (keep running in a separate terminal):

```bash
kubectl port-forward svc/kafka-cluster-kafka-bootstrap -n kafka 9093:9093
```

3. **Run the producer:**

```bash
cd producer
DOTNET_ENVIRONMENT=Development dotnet run
```

`appsettings.Development.json` configures `SecurityProtocol: Ssl` with the cert paths above and sets `EnableInsecureSsl: true` to skip hostname verification (the broker cert won't have `localhost` as a SAN).

#### OAuthBearer (Azure Entra ID)

Authenticate using Azure CLI:

```bash
az login
cd producer && dotnet run
```

`DefaultAzureCredential` will automatically use your Azure CLI credentials.

The producer also supports [.NET user secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for storing sensitive config locally without committing it:

```bash
cd producer
dotnet user-secrets set "AZURE_CLIENT_SECRET" "<your-secret>"
```

### Kubernetes Deployment

There are two Kubernetes deployment examples for each app, depending on your authentication method:

| Auth Method       | Consumer                                         | Producer                                         |
|-------------------|--------------------------------------------------|--------------------------------------------------|
| Workload Identity | `consumer/k8s-deployment-workload-identity.yaml` | `producer/k8s-deployment-workload-identity.yaml` |
| Client Secret     | `consumer/k8s-deployment-secret.yaml`            | `producer/k8s-deployment-secret.yaml`            |
| mTLS              | `consumer/k8s-deployment-mtls.yaml`              | `producer/k8s-deployment-mtls.yaml`              |

#### Option 1: Workload Identity

Uses Azure Workload Identity to authenticate without secrets. The AKS workload identity webhook automatically injects the federated token.

1. **Enable Workload Identity on your AKS cluster:**

```bash
az aks update \
  --name <aks-cluster-name> \
  --resource-group <resource-group> \
  --enable-oidc-issuer \
  --enable-workload-identity
```

2. **Create a Kubernetes Service Account:**

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: kafka-consumer-sa
  namespace: your-namespace
  annotations:
    azure.workload.identity/client-id: "<your-app-registration-client-id>"
```

3. **Deploy your application:**

```bash
kubectl apply -f consumer/k8s-deployment-workload-identity.yaml
kubectl apply -f producer/k8s-deployment-workload-identity.yaml
```

The workload identity webhook automatically injects:
- `AZURE_FEDERATED_TOKEN_FILE` - path to the projected service account token
- Mounts the token at the specified path

#### Option 2: Client Secret

Uses a Kubernetes Secret containing the Azure client secret for authentication. This is useful for non-AKS environments or CI/CD pipelines.

1. **Create the Kubernetes Secret:**

```bash
kubectl create secret generic azure-client-secret \
  --from-literal=client-secret="<your-azure-client-secret>"
```

2. **Deploy your application:**

```bash
kubectl apply -f consumer/k8s-deployment-secret.yaml
kubectl apply -f producer/k8s-deployment-secret.yaml
```

### Azure Container Apps / App Service

For managed Azure services, use Managed Identity:

1. Enable System-assigned or User-assigned Managed Identity on your resource
2. Create a federated credential linking the Managed Identity to your App Registration (if using User-assigned)
3. Set the `AZURE_CLIENT_ID` environment variable to your App Registration's Client ID

## Authentication Flow

```
+-------------------+     +-------------------+     +-------------------+
|   Application     |---->|   Entra ID        |---->|   Kafka Broker    |
|                   |     |                   |     |                   |
| 1. Request token  |     | 2. Validate       |     | 4. Validate       |
|    with OIDC      |     |    federated      |     |    OAuth token    |
|    assertion      |     |    credential     |     |                   |
|                   |<----|                   |     |                   |
|                   |     | 3. Return OAuth   |     |                   |
|                   |     |    token          |     |                   |
+-------------------+     +-------------------+     +-------------------+
```

## Error Handling & Kubernetes Restart Behaviour

The producer uses a fail-fast strategy suited for Kubernetes:

| Condition                    | Behaviour                                                     |
|------------------------------|---------------------------------------------------------------|
| Auth failure                 | Logged to stderr; retried by rdkafka                          |
| 3 consecutive auth failures  | Exits with code **1** → Kubernetes restarts the pod           |
| Produce error                | Retried with exponential backoff (1 s → 2 s → 4 s … max 30 s) |
| 5 consecutive produce errors | Exits with code **1** → Kubernetes restarts the pod           |
| Ctrl+C / SIGTERM             | Flushes producer and exits with code **0**                    |

`terminationGracePeriodSeconds: 30` in the deployment gives the producer time to flush pending messages before Kubernetes force-kills the container.

## Troubleshooting

### "AADSTS70021: No matching federated identity record found"

- Verify the issuer URL matches exactly
- Check the subject claim matches your service account (`system:serviceaccount:<namespace>:<service-account-name>`)
- Ensure the federated credential is configured for the correct namespace and service account

### "AADSTS700024: Client assertion is not within its valid time range"

- Check that your cluster's time is synchronized
- The token may have expired - ensure token refresh is working

### Token acquisition fails locally

- Run `az login` to authenticate
- Verify you have access to the correct tenant: `az account show`
- Try `az account set --subscription <subscription-id>` if you have multiple subscriptions

## Environment Variables

| Variable                             | Description                                                 | Default      |
|--------------------------------------|-------------------------------------------------------------|--------------|
| `DOTNET_ENVIRONMENT`                 | Set to `Development` to load `appsettings.Development.json` | `Production` |
| `HealthPort`                         | Port for `/live` and `/ready` health endpoints              | `8080`       |
| `AZURE_CLIENT_ID`                    | App Registration Client ID (OAuthBearer)                    | —            |
| `AZURE_TENANT_ID`                    | Azure Tenant ID (OAuthBearer)                               | —            |
| `AZURE_CLIENT_SECRET`                | Client secret for service principal auth (OAuthBearer)      | —            |
| `AZURE_FEDERATED_TOKEN_FILE`         | Path to OIDC token, auto-injected by AKS Workload Identity  | —            |
| `Kafka__Ssl__SslCertificateLocation` | Path to client certificate PEM (mTLS)                       | —            |
| `Kafka__Ssl__SslKeyLocation`         | Path to client private key PEM (mTLS)                       | —            |
| `Kafka__Ssl__SslKeyPassword`         | Password for the client private key (mTLS)                  | —            |

### Client Secret Authentication

When `AZURE_CLIENT_SECRET` is set alongside `AZURE_CLIENT_ID` and `AZURE_TENANT_ID`, the application uses `ClientSecretCredential` instead of `DefaultAzureCredential`. This is useful for:

- CI/CD pipelines
- Non-AKS environments without managed identity
- Local testing with a service principal

```bash
export AZURE_CLIENT_ID="<your-client-id>"
export AZURE_TENANT_ID="<your-tenant-id>"
export AZURE_CLIENT_SECRET="<your-client-secret>"
dotnet run
```

Without `AZURE_CLIENT_SECRET`, the app falls back to `DefaultAzureCredential`.
