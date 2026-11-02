# Docker images ko AWS ECR par bhejta hai. Dobara chalane par jo ho chuka usay skip karta hai.
Set-Location D:\enterprise-ai-automation
$env:AWS_PAGER = ""
$REGION = "ap-southeast-2"
$TAG = "v1"

docker info *> $null
if ($LASTEXITCODE -ne 0) { Write-Host "Docker Desktop band hai. Pehle usay kholein." -ForegroundColor Red; exit 1 }

$ACCOUNT = aws sts get-caller-identity --query Account --output text
$ECR = "${ACCOUNT}.dkr.ecr.${REGION}.amazonaws.com"
aws ecr get-login-password --region $REGION | docker login --username AWS --password-stdin $ECR
if ($LASTEXITCODE -ne 0) { Write-Host "ECR login fail" -ForegroundColor Red; exit 1 }

$items = @(
  @{ name = "api";      file = "backend/src/Automation.Api/Dockerfile";    ctx = "backend"  },
  @{ name = "worker";   file = "backend/src/Automation.Worker/Dockerfile"; ctx = "backend"  },
  @{ name = "frontend"; file = "frontend/Dockerfile.aws";                  ctx = "frontend" }
)

foreach ($i in $items) {
  $repo = "ai-review/" + $i.name

  aws ecr describe-repositories --repository-names $repo 2>$null | Out-Null
  if ($LASTEXITCODE -ne 0) {
    aws ecr create-repository --repository-name $repo --image-tag-mutability IMMUTABLE --image-scanning-configuration scanOnPush=true --query repository.repositoryUri --output text
  }

  $have = aws ecr list-images --repository-name $repo --query "imageIds[?imageTag=='$TAG'].imageTag" --output text
  if ($have -eq $TAG) { Write-Host "$repo : $TAG pehle se ECR mein hai, skip"; continue }

  Write-Host "== BUILD $repo" -ForegroundColor Cyan
  docker build --platform linux/amd64 -f $i.file -t "${ECR}/${repo}:${TAG}" $i.ctx
  if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAIL: $repo" -ForegroundColor Red; exit 1 }

  Write-Host "== PUSH $repo" -ForegroundColor Cyan
  docker push "${ECR}/${repo}:${TAG}"
  if ($LASTEXITCODE -ne 0) { Write-Host "PUSH FAIL: $repo" -ForegroundColor Red; exit 1 }
}

Write-Host ""
Write-Host "Nateeja:" -ForegroundColor Green
foreach ($n in "api","worker","frontend") {
  $t = aws ecr list-images --repository-name ai-review/$n --query "imageIds[].imageTag" --output text
  Write-Host ("  {0,-9}: {1}" -f $n, $t)
}