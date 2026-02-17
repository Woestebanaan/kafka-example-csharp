# Kafka Consumer & Producer Example (C#)

.NET 9 Kafka consumer and producer apps that use Azure Entra ID (formerly Azure AD) with federated credentials for authentication.

## Project Structure

- `consumer/` - Kafka consumer that reads messages from a topic
- `producer/` - Kafka producer that publishes the current UTC time every second

## Prerequisites

- .NET 9 SDK
- Azure CLI (for local development)
- Access to an Azure Entra ID tenant

## Configuration

### appsettings.json

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

### Local Development

For local development, authenticate using Azure CLI:

```bash
# Login to Azure
az login

# Verify you're logged in
az account show

# Run the consumer
cd consumer && dotnet run

# Run the producer
cd producer && dotnet run
```

`DefaultAzureCredential` will automatically use your Azure CLI credentials.

### Kubernetes Deployment

There are two Kubernetes deployment examples for each app, depending on your authentication method:

| Auth Method | Consumer | Producer |
|---|---|---|
| Workload Identity | `consumer/k8s-deployment-workload-identity.yaml` | `producer/k8s-deployment-workload-identity.yaml` |
| Client Secret | `consumer/k8s-deployment-secret.yaml` | `producer/k8s-deployment-secret.yaml` |

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

| Variable | Description | Required |
|----------|-------------|----------|
| `AZURE_CLIENT_ID` | App Registration Client ID | Yes (in Kubernetes) |
| `AZURE_TENANT_ID` | Azure Tenant ID | Yes (in Kubernetes) |
| `AZURE_CLIENT_SECRET` | Client secret for service principal auth | No (see below) |
| `AZURE_FEDERATED_TOKEN_FILE` | Path to OIDC token (auto-injected by AKS) | Auto |

### Client Secret Authentication

When `AZURE_TENANT_ID` and `AZURE_CLIENT_SECRET` are set alongside `AZURE_CLIENT_ID`, the application uses `ClientSecretCredential` instead of `DefaultAzureCredential`. This is useful for:

- CI/CD pipelines
- Non-AKS environments without managed identity
- Local testing with a service principal

```bash
export AZURE_CLIENT_ID="<your-client-id>"
export AZURE_TENANT_ID="<your-tenant-id>"
export AZURE_CLIENT_SECRET="<your-client-secret>"
dotnet run
```

Without `AZURE_CLIENT_SECRET` and `AZURE_TENANT_ID`, the app falls back to `DefaultAzureCredential`.
