# Mauro Minella repository for storing samples about<br/>
# `Semantic Kernel` samples

## Environment preparation

### 1. Install Git from its [WEB site](https://git-scm.com/downloads), choosing your operating system

### 2. Open a git/bash command prompt, or make sure that git executable is in the path

### 3. ***CD*** into the base folder for your git repositories
If you do not have one, you may create a folder called `git_repos`

### 4. Use `git` to clone this repo locally
```git clone --branch my-feature-branch --single-branch https://github.com/maurominella/semantic-kernel.git```

### 5. Create a sub-folder of the base `git_repos` called `config` if it does not exist yet
**Before** moving into this folder, just copy the file `credentials_my(template).env` of the cloned repo into it:
- ```cp ./semantic-kernel/credentials_my\(template\).env ./config```

The file `./config/credentials_my.env` -without the final `(template)` in the name- will have to be updated with your own credentials in order to be shared among all repositories.

### 6. ***CD*** into `semantic_kernel` folder of the cloned repository
```cd semantic-kernel```

### 7. Install Miniconda from its [WEB site](https://www.anaconda.com/docs/getting-started/miniconda/install), choosing your operating system

### 8. Open Miniconda bash / prompt, or make sure that conda executable is in the path

### 9. Environment provisioning for Semantic Kernel (`semantic_kernel`)

#### 9.1 Remove the pre-existing conda `semantic_kernel` environment (if exists)
```conda env remove -n semantic_kernel -y```

#### 9.2 Create new Conda Environment `semantic_kernel` with Python 3.13
```conda create -n semantic_kernel python=3.13 -y```

#### 9.3 Activate the `semantic_kernel` environment
```conda activate semantic_kernel```

#### 9.4 Install libraries and dependencies
```pip install -r requirements_semantic-kernel.txt```

#### 9.5 Remove `semantic_kernel` kernel (if exists)
```jupyter kernelspec uninstall semantic_kernel -y```

#### 9.6 Create `semantic_kernel` kernel 
```python -m ipykernel install --name semantic_kernel --user```

#### 9.7 Check kernels list to make sure that `semantic_kernel` exists
```jupyter kernelspec list```
